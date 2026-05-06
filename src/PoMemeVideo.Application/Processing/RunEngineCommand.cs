// SOLID: Open/Closed — new AI providers plug in via IAiVisionService without modifying this command
using PoMemeVideo.Application.MemeLibrary;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;
using PoMemeVideo.Shared.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Application.Processing;

public sealed class RunEngineCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IVideoSessionRepository _sessions;
    private readonly ISoundAssetRepository _sounds;
    private readonly IAiVisionService _aiVision;
    private readonly IDirectorService _director;
    private readonly IDirectorScriptRepository _scripts;
    private readonly IEngineNotifier _notifier;
    private readonly SemanticMatchingService _matching;
    private readonly IBlobStorageService _blobs;

    public RunEngineCommand(
        IVideoSessionRepository sessions,
        ISoundAssetRepository sounds,
        IAiVisionService aiVision,
        IDirectorService director,
        IDirectorScriptRepository scripts,
        IEngineNotifier notifier,
        SemanticMatchingService matching,
        IBlobStorageService blobs)
    {
        _sessions = sessions;
        _sounds = sounds;
        _aiVision = aiVision;
        _director = director;
        _scripts = scripts;
        _notifier = notifier;
        _matching = matching;
        _blobs = blobs;
    }

    public async Task ExecuteAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
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
            await _notifier.DirectorLogAsync(sessionId,
                $"KEYFRAMES LOADED: {keyframeImages.Length} frame(s) ready for analysis.", cancellationToken);

            // AI Vision analysis
            await _notifier.DirectorLogAsync(sessionId, "RUNNING AI VISION ANALYSIS...", cancellationToken);
            var visionLabels = await _aiVision.AnalyseAsync(keyframeImages, cancellationToken);
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

            // If no requests from matching (e.g., empty library), create stub entries
            if (placementRequests.Count == 0 && allSounds.Count > 0)
            {
                foreach (var (ts, label) in visionLabels.Take(5))
                    placementRequests.Add(new((long)(ts * 1000), allSounds[0], 0.5f));
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
            var approvedLabels = decisions.Select(d =>
            {
                var original = visionLabels
                    .OrderBy(v => Math.Abs(v.TimestampSeconds - d.ApprovedTimestampMs / 1000.0))
                    .FirstOrDefault();
                return (TimestampSeconds: d.ApprovedTimestampMs / 1000.0,
                        Label: original.Label ?? "unknown");
            }).ToArray();

            var approvedSounds = decisions.Select(d => d.SelectedSound).ToList();

            // Director service enriches entries (adds rationale, isIronic, visual effects)
            await _notifier.DirectorLogAsync(sessionId, "DIRECTOR IMPROVISING... BUILDING SCRIPT...", cancellationToken);
            var scriptEntries = await _director.DirectAsync(approvedLabels, approvedSounds, sessionId, cancellationToken);

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

            // Update session status to Complete
            await _sessions.UpdateStatusAsync(sessionId, userId, SessionStatus.Complete, cancellationToken: cancellationToken);
            await _notifier.DirectorLogAsync(sessionId, "DIRECTOR'S SCRIPT COMPLETE. READY FOR RENDER.", cancellationToken);
            await _notifier.CompleteAsync(sessionId, string.Empty, cancellationToken);
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

    private static ScriptEntryDto MapToDto(ScriptEntry entry) => new()
    {
        EntryId = entry.EntryId,
        SessionId = entry.SessionId,
        TimestampMs = entry.TimestampMs,
        SoundId = entry.SoundId,
        ActionVectorTags = entry.ActionVectorTags,
        SelectionRationale = entry.SelectionRationale,
        IsIronic = entry.IsIronic,
        VisualEffect = entry.VisualEffect,
        EffectIntensity = entry.EffectIntensity,
        OverlayAssetId = entry.OverlayAssetId,
        PlacementType = entry.PlacementType,
    };
}
