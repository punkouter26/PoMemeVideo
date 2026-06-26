// GoF: Null Object Pattern — deterministic script generation for development/testing
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Api.Features.Processing;

public sealed class MockDirectorService : IDirectorService
{
    private static readonly VisualEffectType[] Effects =
        Enum.GetValues<VisualEffectType>().Where(e => e != VisualEffectType.None).ToArray();

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
        Guid sessionId,
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

            results.Add(new ScriptEntry
            {
                EntryId = Guid.NewGuid(),
                SessionId = sessionId,
                TimestampMs = (long)(ts * 1000),
                SoundId = sound.SoundId,
                SoundName = sound.DisplayName,
                ActionVectorTags = [label],
                SceneDescription = sceneDesc,
                SelectionRationale = $"[MOCK] '{label}' matched '{sound.DisplayName}' — the {(i % 2 == 1 ? "ironic contrast" : "tonal resonance")} amplifies the comedic impact of the scene.",
                IsIronic = i % 2 == 1,
                VisualEffect = Effects[effectIndex++ % Effects.Length],
                EffectIntensity = 0.7 + (i % 3) * 0.1,
                PlacementType = PlacementType.Triggered,
            });
        }

        return Task.FromResult(results.ToArray());
    }
}
