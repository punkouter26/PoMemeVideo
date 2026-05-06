// GoF: Strategy Pattern — timing algorithm encapsulated
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Application.Processing;

public sealed record PlacementRequest(
    long RequestedTimestampMs,
    SoundAsset SelectedSound,
    float CosineScore);

public sealed record TimingDecision(
    long ApprovedTimestampMs,
    SoundAsset SelectedSound,
    PlacementType PlacementType,
    string AuditMessage);

public sealed class TokenBucketTimingService
{
    public const long MinGapMs = 2_000;
    public const long MaxGapMs = 10_000;

    /// <summary>
    /// Applies token-bucket timing constraints to placement requests.
    /// Within the same 2-second window, the request with the highest cosine score wins.
    /// Gaps exceeding MaxGapMs receive a fallback placement.
    /// </summary>
    public IReadOnlyList<TimingDecision> Apply(
        IReadOnlyList<PlacementRequest> requests,
        double videoDurationSeconds,
        SoundAsset? fallbackSound = null)
    {
        // Phase 1: Conflict resolution — keep highest-score per 2-second window
        var byWindow = requests
            .GroupBy(r => r.RequestedTimestampMs / MinGapMs)
            .Select(g =>
            {
                var winner = g.MaxBy(r => r.CosineScore)!;
                return (Request: winner, HadConflict: g.Count() > 1);
            })
            .OrderBy(x => x.Request.RequestedTimestampMs)
            .ToList();

        var decisions = new List<TimingDecision>();
        long lastMs = -MinGapMs;

        foreach (var (req, hadConflict) in byWindow)
        {
            var minAllowedMs = lastMs + MinGapMs;

            if (req.RequestedTimestampMs >= minAllowedMs)
            {
                var type = hadConflict ? PlacementType.ConflictWinner : PlacementType.Triggered;
                var audit = hadConflict
                    ? $"[CONFLICT] Score={req.CosineScore:F2} won window at {req.RequestedTimestampMs}ms"
                    : $"TRIGGERED: t={req.RequestedTimestampMs}ms, gap={req.RequestedTimestampMs - lastMs}ms";

                decisions.Add(new(req.RequestedTimestampMs, req.SelectedSound, type, audit));
                lastMs = req.RequestedTimestampMs;
            }
            else
            {
                // Needs shifting forward — still a ConflictWinner
                var audit = $"[CONFLICT] Shifted {req.RequestedTimestampMs}ms → {minAllowedMs}ms " +
                            $"(gap was {req.RequestedTimestampMs - lastMs}ms < {MinGapMs}ms)";
                decisions.Add(new(minAllowedMs, req.SelectedSound, PlacementType.ConflictWinner, audit));
                lastMs = minAllowedMs;
            }
        }

        // Phase 2: Insert fallbacks for gaps exceeding MaxGapMs
        if (fallbackSound is not null)
        {
            var timestamps = new List<long> { 0 };
            timestamps.AddRange(decisions.Select(d => d.ApprovedTimestampMs));
            timestamps.Add((long)(videoDurationSeconds * 1000));

            var fallbacks = new List<TimingDecision>();
            for (var i = 0; i < timestamps.Count - 1; i++)
            {
                var gapStart = timestamps[i];
                var gapEnd = timestamps[i + 1];
                if (gapEnd - gapStart > MaxGapMs)
                {
                    var fallbackMs = gapStart + MaxGapMs / 2;
                    fallbacks.Add(new(
                        fallbackMs,
                        fallbackSound,
                        PlacementType.Fallback,
                        $"[FALLBACK] Gap {gapEnd - gapStart}ms > {MaxGapMs}ms; sound inserted at {fallbackMs}ms"));
                }
            }

            decisions.AddRange(fallbacks);
        }

        return decisions.OrderBy(d => d.ApprovedTimestampMs).ToList();
    }
}
