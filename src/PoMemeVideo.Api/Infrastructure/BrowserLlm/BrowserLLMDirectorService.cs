// GoF: Mediator — server side of the browser LLM round-trip.
// When the active provider is "BrowserLLM" this service:
//   1. Serialises the inference payload and sends it to the browser via SignalR.
//   2. Awaits a TaskCompletionSource that the /browser-director-result endpoint resolves
//      once the browser has finished running Transformers.js locally.
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Infrastructure.BrowserLlm;

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
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ScriptEntry[]>> _pending = new();

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
        Guid sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<ScriptEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[sessionId] = tcs;

        // Serialise just what the browser needs
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            visionLabels = visionLabels.Select(v => new { v.TimestampSeconds, v.Label }),
            sounds = topCandidates.Select(s => new { s.SoundId, s.DisplayName, Tags = s.ActionVectorTags }),
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
    public bool TryResolve(Guid sessionId, BrowserDirectorResultDto result)
    {
        if (!_pending.TryRemove(sessionId, out var tcs))
            return false;

        try
        {
            var entries = result.Entries.Select(e => new ScriptEntry
            {
                EntryId = Guid.NewGuid(),
                SessionId = sessionId,
                TimestampMs = e.TimestampMs,
                SoundId = e.SoundId,
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
