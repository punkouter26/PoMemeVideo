// SOLID: Open/Closed — new AI providers plug in via IAiVisionService without modifying this command
using Microsoft.Extensions.Logging;
using PoMemeVideo.Application.MemeLibrary;
using PoMemeVideo.Application.Rendering;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;
using PoMemeVideo.Shared.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Application.Processing;

public sealed class RunEngineCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowDuplicateProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly IVideoSessionRepository _sessions;
    private readonly ISoundAssetRepository _sounds;
    private readonly IAiVisionService _aiVision;
    private readonly IDirectorService _director;
    private readonly IDirectorScriptRepository _scripts;
    private readonly IEngineNotifier _notifier;
    private readonly SemanticMatchingService _matching;
    private readonly IBlobStorageService _blobs;
    private readonly RenderVideoCommand _render;
    private readonly ILogger<RunEngineCommand> _logger;

    public RunEngineCommand(
        IVideoSessionRepository sessions,
        ISoundAssetRepository sounds,
        IAiVisionService aiVision,
        IDirectorService director,
        IDirectorScriptRepository scripts,
        IEngineNotifier notifier,
        SemanticMatchingService matching,
        IBlobStorageService blobs,
        RenderVideoCommand render,
        ILogger<RunEngineCommand> logger)
    {
        _sessions = sessions;
        _sounds = sounds;
        _aiVision = aiVision;
        _director = director;
        _scripts = scripts;
        _notifier = notifier;
        _matching = matching;
        _blobs = blobs;
        _render = render;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var diagRunId = Guid.NewGuid().ToString("N")[..8];
        var diagPrefix = $"[diag:{diagRunId}][session:{sessionId}]";
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = sessionId,
            ["DiagRunId"] = diagRunId,
        });

        _logger.LogInformation("{DiagPrefix} RunEngineCommand started.", diagPrefix);

        var session = await _sessions.GetByIdAsync(sessionId, userId, cancellationToken)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");

        if (session.Status != SessionStatus.Ingesting)
            throw new InvalidOperationException($"Session {sessionId} is not in Ingesting state (current: {session.Status}).");

        await _sessions.UpdateStatusAsync(sessionId, userId, SessionStatus.Processing, cancellationToken: cancellationToken);

        // Start hardware-metrics background timer
        using var metricsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var metricsTask = EmitHardwareMetricsAsync(sessionId, metricsCts.Token);

        try
        {
            await _notifier.DirectorLogAsync(sessionId, "DIRECTOR ONLINE. INITIALISING ENGINE...", cancellationToken);

            // Load keyframe images from blob storage (client-side extracted frames)
            var keyframeImages = await LoadKeyframeImagesAsync(sessionId, cancellationToken);
            _logger.LogInformation("Session {SessionId}: {FrameCount} keyframe(s) loaded from blob storage.", sessionId, keyframeImages.Length);
            await _notifier.DirectorLogAsync(sessionId,
                $"KEYFRAMES LOADED: {keyframeImages.Length} frame(s) ready for analysis.", cancellationToken);

            // Use pre-computed vision labels if available (written during frame upload), otherwise re-analyse
            var visionLabels = await LoadPrecomputedVisionLabelsAsync(sessionId, cancellationToken);
            if (visionLabels.Length > 0)
            {
                _logger.LogInformation("Session {SessionId}: Using {LabelCount} pre-computed vision label(s).", sessionId, visionLabels.Length);
                await _notifier.DirectorLogAsync(sessionId,
                    $"VISION LABELS LOADED: {visionLabels.Length} pre-analysed trigger(s) ready.", cancellationToken);
            }
            else if (keyframeImages.Length > 0)
            {
                // Fallback: run vision analysis now (no pre-computed labels)
                await _notifier.DirectorLogAsync(sessionId, "RUNNING AI VISION ANALYSIS...", cancellationToken);
                visionLabels = await _aiVision.AnalyseAsync(keyframeImages, cancellationToken);
            }
            else
            {
                // No keyframes and no pre-computed labels — skip vision entirely, time-based fallback will apply
                _logger.LogInformation("Session {SessionId}: No keyframes available — skipping vision analysis, time-based placement will be used.", sessionId);
                await _notifier.DirectorLogAsync(sessionId, "NO KEYFRAMES — SKIPPING VISION ANALYSIS. TIME-BASED PLACEMENT WILL BE USED.", cancellationToken);
            }

            _logger.LogInformation("Session {SessionId}: Vision returned {LabelCount} label(s): {Labels}",
                sessionId, visionLabels.Length,
                string.Join(", ", visionLabels.Select(v => $"t={v.TimestampSeconds:F1}s → \"{v.Label}\"")));
            await _notifier.DirectorLogAsync(sessionId,
                $"ACTION DETECTED: {visionLabels.Length} semantic trigger(s) identified.", cancellationToken);

            // Load sound library
            var allSounds = await _sounds.LoadAllAsync(cancellationToken);
            await _notifier.DirectorLogAsync(sessionId,
                $"SOUND LIBRARY: {allSounds.Count} asset(s) loaded from cache.", cancellationToken);

            // Semantic matching per label
            var placementRequests = new List<PlacementRequest>();
            foreach (var (ts, label) in visionLabels)
            {
                var candidates = await _matching.GetTopCandidatesAsync(label, topN: 3, cancellationToken);
                if (candidates.Count == 0)
                {
                    await _notifier.DirectorLogAsync(sessionId,
                        $"SCANNING... t={ts:F1}s | ACTION: [{label.ToUpperInvariant()}] — no candidates found, skipping.", cancellationToken);
                    continue;
                }

                var best = candidates[0];
                await _notifier.DirectorLogAsync(sessionId,
                    $"SCANNING... t={ts:F1}s | ACTION: [{label.ToUpperInvariant()}]", cancellationToken);
                await _notifier.DirectorLogAsync(sessionId,
                    $"SEARCHING SOUND LIBRARY... {candidates.Count} candidate(s) found", cancellationToken);
                await _notifier.DirectorLogAsync(sessionId,
                    $"SELECTED: {best.Sound.DisplayName} (accuracy={best.Score:F2})", cancellationToken);

                placementRequests.Add(new((long)(ts * 1000), best.Sound, best.Score));
            }

            // Time-based fallback: when no placements exist (e.g. no keyframes or no library matches),
            // place a meme sound every 2 seconds up to 10 seconds, with at least one sound always placed.
            if (placementRequests.Count == 0 && allSounds.Count > 0)
            {
                var videoMs = (long)(session.VideoDurationSeconds * 1000);
                var windowMs = Math.Min(videoMs, 10_000L);
                var times = new List<long>();
                for (var t = 0L; t < windowMs; t += 2_000L)
                    times.Add(t);
                if (times.Count == 0) times.Add(0); // always at least one

                for (var i = 0; i < times.Count; i++)
                {
                    // Deduplicate: prefer sounds not already in this session
                    var usedSoundIds = placementRequests.Select(p => p.SelectedSound.SoundId).ToHashSet();
                    var available = allSounds.Where(s => !usedSoundIds.Contains(s.SoundId)).ToList();
                    if (available.Count == 0) available = allSounds.ToList(); // all used — allow repeats
                    var sound = available[Random.Shared.Next(available.Count)];
                    placementRequests.Add(new(times[i], sound, 0.5f));
                }

                await _notifier.DirectorLogAsync(sessionId,
                    $"TIME-BASED FALLBACK: {placementRequests.Count} placement(s) every 2s (video={session.VideoDurationSeconds:F1}s, cap=10s).", cancellationToken);
            }

            // Apply token-bucket timing constraints
            var fallbackSound = allSounds.Count > 0 ? allSounds[0] : null;
            var timingService = new TokenBucketTimingService();
            var decisions = timingService.Apply(placementRequests, session.VideoDurationSeconds, fallbackSound);

            await _notifier.DirectorLogAsync(sessionId,
                $"TOKEN BUCKET: {decisions.Count} placement(s) approved.", cancellationToken);

            // Audit timing events
            foreach (var d in decisions)
            {
                await _notifier.AuditAsync(sessionId,
                    d.PlacementType != PlacementType.Triggered
                        ? d.AuditMessage
                        : $"PLACED: {d.SelectedSound.DisplayName} @ {d.ApprovedTimestampMs}ms [{d.PlacementType}]",
                    cancellationToken);
            }

            // Build approved vision labels for the Director
            var videoDurSec = session.VideoDurationSeconds > 0 ? session.VideoDurationSeconds : 1.0;
            var approvedLabels = decisions.Select(d =>
            {
                var original = visionLabels
                    .OrderBy(v => Math.Abs(v.TimestampSeconds - d.ApprovedTimestampMs / 1000.0))
                    .FirstOrDefault();
                var label = original.Label;
                if (string.IsNullOrWhiteSpace(label) || label == "unknown")
                {
                    // Replace "unknown" with a positional description so the AI director can reason about timing.
                    var relPos = d.ApprovedTimestampMs / 1000.0 / videoDurSec;
                    label = relPos < 0.25 ? "opening scene" : relPos < 0.6 ? "mid-video action" : "final moments";
                }
                return (TimestampSeconds: d.ApprovedTimestampMs / 1000.0, Label: label);
            }).ToArray();

            var approvedSounds = decisions.Select(d => d.SelectedSound).ToList();

            _logger.LogInformation(
                "{DiagPrefix} Director input prepared. ApprovedLabels={LabelCount}, ApprovedSounds={SoundCount}, FirstLabel={FirstLabel}, FirstSound={FirstSound}",
                diagPrefix,
                approvedLabels.Length,
                approvedSounds.Count,
                approvedLabels.FirstOrDefault().Label,
                approvedSounds.FirstOrDefault()?.DisplayName);

            // Director service enriches entries (adds rationale, isIronic, visual effects, scene descriptions)
            await _notifier.DirectorLogAsync(sessionId, "DIRECTOR IMPROVISING... BUILDING SCRIPT...", cancellationToken);
            _logger.LogInformation(
                "{DiagPrefix} Entering IDirectorService.DirectAsync (Provider delegated by SwitchingDirectorService). CancellationRequested={CancellationRequested}",
                diagPrefix,
                cancellationToken.IsCancellationRequested);
            var directorStopwatch = Stopwatch.StartNew();
            ScriptEntry[] scriptEntries;
            try
            {
                scriptEntries = await _director.DirectAsync(approvedLabels, approvedSounds, sessionId, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // BrowserLLM can time out if the client page disconnects or misses the SignalR request.
                // Degrade gracefully to deterministic fallback entries instead of failing the entire session.
                _logger.LogWarning(ex,
                    "{DiagPrefix} Director call timed out; continuing with deterministic fallback entries.",
                    diagPrefix);
                await _notifier.DirectorLogAsync(sessionId,
                    "DIRECTOR TIMEOUT — CONTINUING WITH FALLBACK SCRIPT.",
                    cancellationToken);
                scriptEntries = [];
            }
            directorStopwatch.Stop();
            _logger.LogInformation(
                "{DiagPrefix} IDirectorService.DirectAsync returned {EntryCount} entry/entries in {ElapsedMs} ms.",
                diagPrefix,
                scriptEntries.Length,
                directorStopwatch.ElapsedMilliseconds);

            // Fallback: if the director returned nothing but we have approved placements, build basic entries directly
            if (scriptEntries.Length == 0 && decisions.Count > 0)
            {
                _logger.LogWarning("Session {SessionId}: Director returned 0 entries despite {Count} approved placement(s). Falling back to basic entries.", sessionId, decisions.Count);
                await _notifier.DirectorLogAsync(sessionId, $"[DIRECTOR FALLBACK] LLM returned no entries — building {decisions.Count} basic placement(s) from approved decisions.", cancellationToken);
                scriptEntries = decisions.Select((d, i) => new ScriptEntry
                {
                    EntryId = Guid.NewGuid(),
                    SessionId = sessionId,
                    TimestampMs = d.ApprovedTimestampMs,
                    SoundId = d.SelectedSound.SoundId,
                    SoundName = d.SelectedSound.DisplayName,
                    ActionVectorTags = d.SelectedSound.ActionVectorTags,
                    SelectionRationale = i < approvedLabels.Length && approvedLabels[i].Label == "unknown"
                        ? "[FALLBACK] No scene context detected — time-based placement."
                        : $"[FALLBACK] Direct semantic match for '{(i < approvedLabels.Length ? approvedLabels[i].Label : d.SelectedSound.DisplayName)}'.",
                    SceneDescription = i < approvedLabels.Length ? approvedLabels[i].Label : string.Empty,
                    PlacementType = d.PlacementType,
                }).ToArray();
            }

            // Populate SoundName from the approved sound list (by matching SoundId)
            var soundNameMap = approvedSounds.DistinctBy(s => s.SoundId).ToDictionary(s => s.SoundId, s => s.DisplayName);
            foreach (var entry in scriptEntries)
            {
                if (string.IsNullOrEmpty(entry.SoundName) && soundNameMap.TryGetValue(entry.SoundId, out var name))
                    entry.SoundName = name;
            }

            // Override timestamps and placement types from timing decisions
            for (var i = 0; i < scriptEntries.Length && i < decisions.Count; i++)
            {
                scriptEntries[i].TimestampMs = decisions[i].ApprovedTimestampMs;
                scriptEntries[i].PlacementType = decisions[i].PlacementType;
            }

            // Persist DirectorScript
            var script = new DirectorScript
            {
                SessionId = sessionId,
                TotalSoundCount = scriptEntries.Length,
                AverageDensitySeconds = scriptEntries.Length > 0
                    ? session.VideoDurationSeconds / scriptEntries.Length
                    : 0,
                EntriesJson = JsonSerializer.Serialize(scriptEntries, JsonOpts),
            };
            await _scripts.SaveAsync(script, cancellationToken);

            await _notifier.DirectorLogAsync(sessionId,
                $"DIRECTOR'S SCRIPT PERSISTED. {scriptEntries.Length} entry/entries.", cancellationToken);

            // Stream each ScriptEntry to client
            foreach (var entry in scriptEntries)
            {
                var dto = MapToDto(entry);
                await _notifier.DirectorScriptAsync(sessionId, dto, cancellationToken);
                await _notifier.DirectorLogAsync(sessionId,
                    $"SCRIPT ENTRY: t={entry.TimestampMs}ms | {entry.PlacementType} | {entry.SelectionRationale[..Math.Min(60, entry.SelectionRationale.Length)]}", cancellationToken);
            }

            await _notifier.DirectorLogAsync(sessionId, "DIRECTOR'S SCRIPT COMPLETE. STARTING RENDER...", cancellationToken);

            // Hand off to render pipeline — FFmpegRenderService queues the job,
            // encodes meme sounds into the video, uploads output, then fires CompleteAsync.
            await _render.ExecuteAsync(sessionId, userId, session, script, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            await _sessions.UpdateStatusAsync(sessionId, userId, SessionStatus.Error, ex.Message, cancellationToken: default);
            await _notifier.ErrorAsync(sessionId, $"ENGINE ERROR: {ex.Message}", default);
            throw;
        }
        finally
        {
            await metricsCts.CancelAsync();
            try { await metricsTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task EmitHardwareMetricsAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var inferenceLatencyMs = 50.0 + Random.Shared.NextDouble() * 150.0;
            var cpuLoadPercent = 20.0 + Random.Shared.NextDouble() * 60.0;
            await _notifier.HardwareMetricsAsync(sessionId, inferenceLatencyMs, cpuLoadPercent, cancellationToken);
        }
    }

    private async Task<string[]> LoadKeyframeImagesAsync(Guid sessionId, CancellationToken ct)
    {
        var prefix = $"sessions/{sessionId}/frames/";
        var blobs = new List<string>();

        try
        {
            await foreach (var path in _blobs.ListBlobsByPrefixAsync(prefix, ct))
                blobs.Add(path);
        }
        catch
        {
            // No keyframes — AI mock service ignores images
        }

        if (blobs.Count == 0) return [];

        var images = new List<string>();
        foreach (var path in blobs.OrderBy(p => p))
        {
            try
            {
                using var stream = await _blobs.StreamBlobAsync(path, ct);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                images.Add(Convert.ToBase64String(ms.ToArray()));
            }
            catch
            {
                // Skip unreadable blobs
            }
        }

        return [.. images];
    }

    private async Task<(double TimestampSeconds, string Label)[]> LoadPrecomputedVisionLabelsAsync(
        Guid sessionId, CancellationToken ct)
    {
        try
        {
            var path = $"sessions/{sessionId}/vision-labels.json";
            using var stream = await _blobs.StreamBlobAsync(path, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            var items = System.Text.Json.JsonSerializer.Deserialize<VisionLabelItem[]>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return items is null ? [] : items.Select(x => (x.TimestampSeconds, x.Label)).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private sealed record VisionLabelItem(
        [property: System.Text.Json.Serialization.JsonPropertyName("timestamp_seconds")] double TimestampSeconds,
        [property: System.Text.Json.Serialization.JsonPropertyName("label")] string Label);

    private static ScriptEntryDto MapToDto(ScriptEntry entry) => new()
    {
        EntryId = entry.EntryId,
        SessionId = entry.SessionId,
        TimestampMs = entry.TimestampMs,
        SoundId = entry.SoundId,
        SoundName = entry.SoundName,
        ActionVectorTags = entry.ActionVectorTags,
        SceneDescription = entry.SceneDescription,
        SelectionRationale = entry.SelectionRationale,
        IsIronic = entry.IsIronic,
        VisualEffect = entry.VisualEffect,
        EffectIntensity = entry.EffectIntensity,
        OverlayAssetId = entry.OverlayAssetId,
        PlacementType = entry.PlacementType,
    };
}
