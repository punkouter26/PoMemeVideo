// GoF: Null Object Pattern — deterministic script generation for development/testing
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Infrastructure.Mock;

public sealed class MockDirectorService : IDirectorService
{
    private static readonly VisualEffectType[] Effects =
        Enum.GetValues<VisualEffectType>().Where(e => e != VisualEffectType.None).ToArray();

    public Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ScriptEntry>();
        var effectIndex = 0;

        for (var i = 0; i < visionLabels.Length; i++)
        {
            var (ts, label) = visionLabels[i];
            var sound = topCandidates.Count > i ? topCandidates[i] : topCandidates[0];

            results.Add(new ScriptEntry
            {
                EntryId = Guid.NewGuid(),
                SessionId = sessionId,
                TimestampMs = (long)(ts * 1000),
                SoundId = sound.SoundId,
                ActionVectorTags = [label],
                SelectionRationale = $"[MOCK] '{label}' matched '{sound.DisplayName}' by semantic tag overlap.",
                IsIronic = i % 2 == 1,
                VisualEffect = Effects[effectIndex++ % Effects.Length],
                EffectIntensity = 0.7 + (i % 3) * 0.1,
                PlacementType = PlacementType.Triggered,
            });
        }

        return Task.FromResult(results.ToArray());
    }
}
