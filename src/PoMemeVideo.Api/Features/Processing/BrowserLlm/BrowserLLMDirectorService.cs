// GoF: Mediator — server side of the browser LLM round-trip.
// When the active provider is "BrowserLLM" this service:
//   1. Serialises the inference payload and sends it to the browser via SignalR.
//   2. Awaits a TaskCompletionSource that the /browser-director-result endpoint resolves
//      once the browser has finished running Transformers.js locally.
using PoMemeVideo.Shared.Enums;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.Features.Processing;

public sealed class BrowserLLMDirectorService : IDirectorService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowDuplicateProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    // Keyed by sessionId; resolved by the POST endpoint when the browser responds.
    private readonly ConcurrentDictionary<SessionId, TaskCompletionSource<ScriptEntry[]>> _pending = new();

    private readonly IEngineNotifier _notifier;
    private readonly RuntimeAiSettings _settings;

    public BrowserLLMDirectorService(IEngineNotifier notifier, RuntimeAiSettings settings)
    {
        _notifier = notifier;
        _settings = settings;
    }

    public async Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        SessionId sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default)
    {
        // Fail fast when the chosen ONNX model has no weights on disk. Without this check the
        // call sits on a TaskCompletionSource for the full 90 s timeout before the engine falls
        // back to deterministic entries — the user sees "AI Directing…" spin for a minute and
        // 30 s before anything happens. Inspecting the model manifest once at startup lets us
        // short-circuit with a clear error so the user knows to either download weights or
        // switch provider.
        if (!IsLocalModelReady(_settings.BrowserLLMModel))
        {
            throw new InvalidOperationException(
                $"BrowserLLM model '{_settings.BrowserLLMModel}' is missing required local assets. "
                + "Run 'python scripts/download-models.py' or switch the provider to AiFoundry.");
        }

        var tcs = new TaskCompletionSource<ScriptEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[sessionId] = tcs;

        // Serialise just what the browser needs
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            visionLabels = visionLabels.Select(v => new { v.TimestampSeconds, v.Label }),
            sounds = topCandidates.Select(s => new
            {
                s.SoundId,
                s.DisplayName,
                Tags = s.ActionVectorTags,
                UseCase = string.IsNullOrWhiteSpace(s.UseCase) ? null : s.UseCase,
                Priority = s.Priority ? true : (bool?)null,
            }),
            modelId = _settings.BrowserLLMModel,
        }, JsonOpts);

        await _notifier.BrowserLLMInferenceRequestAsync(sessionId, payload, cancellationToken);

        // Cancel the wait if the request is cancelled or times out after 90 s
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token);

        timeout.Token.Register(() =>
            tcs.TrySetException(new OperationCanceledException("BrowserLLM inference timed out (90 s).")));

        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Called by the POST /browser-director-result endpoint to deliver the browser's answer.
    /// </summary>
    public bool TryResolve(SessionId sessionId, BrowserDirectorResultDto result)
    {
        if (!_pending.TryRemove(sessionId, out var tcs))
            return false;

        try
        {
            var entries = result.Entries.Select(e => new ScriptEntry
            {
                EntryId = EntryId.New(),
                SessionId = sessionId,
                TimestampMs = e.TimestampMs,
                SoundId = new SoundId(e.SoundId),
                ActionVectorTags = e.ActionVectorTags,
                SelectionRationale = e.SelectionRationale,
                IsIronic = e.IsIronic,
                VisualEffect = e.VisualEffect,
                EffectIntensity = e.EffectIntensity,
                PlacementType = PlacementType.Triggered,
            }).ToArray();

            tcs.TrySetResult(entries);
            return true;
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            return false;
        }
    }

    /// <summary>
    /// True when the requested local model directory exists under MODEL/. The client-side
    /// runtime probe in browser-llm.js is authoritative — this server-side check is a
    /// best-effort fast-fail so a missing-model run fails in milliseconds instead of waiting
    /// the full 90 s timeout for the browser to report the missing weights.
    /// </summary>
    private static bool IsLocalModelReady(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "MODEL", modelId),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "MODEL", modelId),
            Path.Combine(Directory.GetCurrentDirectory(), "MODEL", modelId),
        };

        return candidates.Any(c => Directory.Exists(c));
    }
}

// ── DTOs used by the POST endpoint ──────────────────────────────────────────

public sealed record BrowserDirectorResultDto(BrowserScriptEntryDto[] Entries);

public sealed record BrowserScriptEntryDto(
    long TimestampMs,
    Guid SoundId,
    string[] ActionVectorTags,
    string SelectionRationale,
    bool IsIronic,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    VisualEffectType? VisualEffect,
    double? EffectIntensity);
