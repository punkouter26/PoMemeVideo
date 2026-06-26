using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.UnitTests.Processing;

public sealed class TokenBucketTimingServiceTests
{
    private readonly TokenBucketTimingService _svc = new();

    private static SoundAsset MakeSound(string name = "TestSound") => new()
    {
        SoundId = Guid.NewGuid(),
        DisplayName = name,
        DurationMs = 800,
        BlobUrl = "https://example.com/test.mp3",
        ActionVectorTags = ["test"],
    };

    // ── Minimum gap enforcement ───────────────────────────────────────────

    [Fact]
    public void Apply_TwoRequestsWithinMinGap_HigherScoreWins()
    {
        var sound1 = MakeSound("Sound1");
        var sound2 = MakeSound("Sound2");

        var requests = new List<PlacementRequest>
        {
            new(1000, sound1, 0.6f),  // window: 0
            new(1500, sound2, 0.9f),  // same window: 0 (1500/2000 == 0)
        };

        var decisions = _svc.Apply(requests, videoDurationSeconds: 30.0);

        // One decision; the higher-score sound wins
        Assert.Single(decisions);
        Assert.Equal(sound2.SoundId, decisions[0].SelectedSound.SoundId);
        Assert.Equal(PlacementType.ConflictWinner, decisions[0].PlacementType);
    }

    [Fact]
    public void Apply_TwoRequestsExceedingMinGap_BothApproved()
    {
        var sound1 = MakeSound("Sound1");
        var sound2 = MakeSound("Sound2");

        var requests = new List<PlacementRequest>
        {
            new(0, sound1, 0.7f),
            new(3000, sound2, 0.7f),  // 3000ms gap > 2000ms min
        };

        var decisions = _svc.Apply(requests, videoDurationSeconds: 10.0);

        Assert.Equal(2, decisions.Count);
        Assert.All(decisions, d => Assert.Equal(PlacementType.Triggered, d.PlacementType));
    }

    // ── Conflict resolution picks highest-score candidate ─────────────────

    [Fact]
    public void Apply_ConflictResolution_PicksHighestScore()
    {
        var sounds = Enumerable.Range(1, 4).Select(i => MakeSound($"Sound{i}")).ToList();

        // All 4 requests within the same 2-second window (0–1999ms)
        var requests = sounds.Select((s, i) =>
            new PlacementRequest(i * 400, s, (i + 1) * 0.2f)).ToList();

        var decisions = _svc.Apply(requests, videoDurationSeconds: 30.0);

        Assert.Single(decisions);
        Assert.Equal(sounds[3].SoundId, decisions[0].SelectedSound.SoundId); // score 0.8f is highest
        Assert.Equal(PlacementType.ConflictWinner, decisions[0].PlacementType);
    }

    // ── PlacementType set correctly ────────────────────────────────────────

    [Fact]
    public void Apply_SingleRequest_PlacementIsTriggered()
    {
        var sound = MakeSound();
        var requests = new List<PlacementRequest> { new(5000, sound, 0.8f) };

        var decisions = _svc.Apply(requests, videoDurationSeconds: 30.0);

        Assert.Single(decisions);
        Assert.Equal(PlacementType.Triggered, decisions[0].PlacementType);
    }

    // ── MaxGap fallback trigger ────────────────────────────────────────────

    [Fact]
    public void Apply_LongGapExceedsMaxGap_InsertsFallback()
    {
        var sound = MakeSound("Triggered");
        var fallback = MakeSound("Fallback");

        // Only one triggered sound at 0ms; video is 30s (30,000ms > MaxGapMs after t=0)
        var requests = new List<PlacementRequest> { new(0, sound, 0.9f) };

        var decisions = _svc.Apply(requests, videoDurationSeconds: 30.0, fallbackSound: fallback);

        // Should have at least one fallback for the 30,000ms gap after t=0
        var fallbacks = decisions.Where(d => d.PlacementType == PlacementType.Fallback).ToList();
        Assert.NotEmpty(fallbacks);
        Assert.All(fallbacks, f => Assert.Equal(fallback.SoundId, f.SelectedSound.SoundId));
    }

    [Fact]
    public void Apply_GapExactlyAtMaxGap_NoFallback()
    {
        var sound1 = MakeSound("S1");
        var sound2 = MakeSound("S2");

        // Exactly MaxGapMs apart (10,000ms) — should NOT trigger fallback
        var requests = new List<PlacementRequest>
        {
            new(0, sound1, 0.8f),
            new(TokenBucketTimingService.MaxGapMs, sound2, 0.8f),
        };

        var decisions = _svc.Apply(requests, videoDurationSeconds: 11.0, fallbackSound: MakeSound("Fallback"));

        var fallbacks = decisions.Where(d => d.PlacementType == PlacementType.Fallback).ToList();
        Assert.Empty(fallbacks);
    }

    // ── AuditMessage content ───────────────────────────────────────────────

    [Fact]
    public void Apply_ConflictWinner_AuditMessageContainsConflict()
    {
        var sound = MakeSound();
        var requests = new List<PlacementRequest>
        {
            new(0, sound, 0.5f),
            new(500, MakeSound("Other"), 0.9f),
        };

        var decisions = _svc.Apply(requests, videoDurationSeconds: 10.0);

        Assert.Single(decisions);
        Assert.Contains("[CONFLICT]", decisions[0].AuditMessage);
    }

    [Fact]
    public void Apply_Fallback_AuditMessageContainsFallback()
    {
        var sound = MakeSound();
        var fallback = MakeSound("FB");
        var requests = new List<PlacementRequest> { new(0, sound, 0.9f) };

        var decisions = _svc.Apply(requests, videoDurationSeconds: 30.0, fallbackSound: fallback);

        var fb = decisions.First(d => d.PlacementType == PlacementType.Fallback);
        Assert.Contains("[FALLBACK]", fb.AuditMessage);
    }
}
