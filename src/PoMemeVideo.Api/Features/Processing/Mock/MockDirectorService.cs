// GoF: Null Object Pattern — deterministic script generation for development/testing
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Api.Features.Processing;

public sealed class MockDirectorService : IDirectorService
{
    // SnapZoom is retired — its crop+scale was applied to the base chain (the whole video) instead
    // of the cue's window, so it is excluded here rather than merely left unused.
    private static readonly VisualEffectType[] Effects =
        Enum.GetValues<VisualEffectType>()
            .Where(e => e != VisualEffectType.None && e != VisualEffectType.SnapZoom)
            .ToArray();

    private static readonly string[] SceneDescriptions =
    [
        "The subject lurches forward unexpectedly, arms outstretched, completely caught off guard.",
        "An awkward silence descends as everyone in frame slowly turns to look directly at the camera.",
        "The protagonist executes a wildly overconfident move that instantly backfires in spectacular fashion.",
        "Someone trips over absolutely nothing and enters full-body chaos mode.",
        "A dramatic reveal unfolds — the reaction is exactly as unhinged as the situation deserves.",
        "Pure unbridled joy erupts as something inexplicably goes exactly right for once.",
    ];

    public Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        SessionId sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ScriptEntry>();
        var effectIndex = 0;
        var descIndex = 0;

        for (var i = 0; i < visionLabels.Length; i++)
        {
            var (ts, label) = visionLabels[i];
            var sound = topCandidates.Count > i ? topCandidates[i] : topCandidates[0];
            var sceneDesc = SceneDescriptions[descIndex++ % SceneDescriptions.Length];

                var effect = Effects[effectIndex++ % Effects.Length];
                results.Add(new ScriptEntry
                {
                    EntryId = EntryId.New(),
                    SessionId = sessionId,
                    TimestampMs = (long)(ts * 1000),
                    SoundId = sound.SoundId,
                    SoundName = sound.DisplayName,
                    ActionVectorTags = [label],
                    SceneDescription = sceneDesc,
                    SelectionRationale = $"[MOCK] '{label}' matched '{sound.DisplayName}' — the {(i % 2 == 1 ? "ironic contrast" : "tonal resonance")} amplifies the comedic impact of the scene.",
                    IsIronic = i % 2 == 1,
                    VisualEffect = effect,
                    EffectIntensity = 0.7 + (i % 3) * 0.1,
                    OverlayAssetId = effect == VisualEffectType.Overlay ? "deal-with-it" : null,
                    OverlayX = effect == VisualEffectType.Overlay ? 0.5 : null,
                    OverlayY = effect == VisualEffectType.Overlay ? 0.3 : null,
                    OverlayScale = effect == VisualEffectType.Overlay ? 1.0 : null,
                    PlacementType = PlacementType.Triggered,
                });
            }

        return Task.FromResult(results.ToArray());
    }
}
