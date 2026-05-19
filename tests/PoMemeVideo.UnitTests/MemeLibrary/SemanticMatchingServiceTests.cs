using NSubstitute;
using PoMemeVideo.Application.MemeLibrary;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;

namespace PoMemeVideo.UnitTests.MemeLibrary;

public sealed class SemanticMatchingServiceTests
{
    private readonly ISoundAssetRepository _repo = Substitute.For<ISoundAssetRepository>();
    private readonly SemanticMatchingService _svc;

    public SemanticMatchingServiceTests()
    {
        _svc = new SemanticMatchingService(_repo);
    }

    private static SoundAsset MakeSound(string name, params string[] tags) => new()
    {
        SoundId = Guid.NewGuid(),
        DisplayName = name,
        DurationMs = 800,
        BlobUrl = "https://example.com/test.mp3",
        ActionVectorTags = tags,
    };

    // ── Cosine similarity returns correct ranking ─────────────────────────

    [Fact]
    public async Task GetTopCandidatesAsync_ReturnsHighestScoringFirst()
    {
        var impact = MakeSound("Vine Boom", "impact", "boom", "fail");
        var laugh = MakeSound("Laugh Track", "laugh", "funny", "happy");
        var pop = MakeSound("Pop Sound", "pop", "soft");

        _repo.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoundAsset> { impact, laugh, pop }.AsReadOnly());

        // Query strongly matching "impact boom" — should rank Vine Boom first
        var results = await _svc.GetTopCandidatesAsync("impact boom fail", topN: 3);

        Assert.NotEmpty(results);
        Assert.Equal(impact.SoundId, results[0].Sound.SoundId);
    }

    [Fact]
    public async Task GetTopCandidatesAsync_Top3SelectionCorrect()
    {
        var sounds = new List<SoundAsset>
        {
            MakeSound("A", "fall", "trip", "down"),
            MakeSound("B", "laugh", "funny"),
            MakeSound("C", "boom", "impact", "fall"),
            MakeSound("D", "music", "slow", "trip"),  // has "trip" → non-zero score
            MakeSound("E", "voice", "speech"),
        };

        _repo.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(sounds.AsReadOnly());

        var results = await _svc.GetTopCandidatesAsync("fall down trip", topN: 3);

        Assert.Equal(3, results.Count);

        // Top result must be one of the sounds containing "fall"
        Assert.Contains(results, r => r.Sound.ActionVectorTags.Contains("fall"));
    }

    // ── Empty library ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopCandidatesAsync_EmptyLibrary_ReturnsEmpty()
    {
        _repo.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SoundAsset>().AsReadOnly());

        var results = await _svc.GetTopCandidatesAsync("anything", topN: 3);

        Assert.Empty(results);
    }

    // ── TopN capped to available sounds ──────────────────────────────────

    [Fact]
    public async Task GetTopCandidatesAsync_RequestMoreThanAvailable_ReturnsAll()
    {
        var sounds = new List<SoundAsset>
        {
            MakeSound("A", "boom"),
            MakeSound("B", "boom"),
        };

        _repo.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(sounds.AsReadOnly());

        var results = await _svc.GetTopCandidatesAsync("boom", topN: 10);

        Assert.Equal(2, results.Count);
    }

    // ── Scores are in descending order ────────────────────────────────────

    [Fact]
    public async Task GetTopCandidatesAsync_ScoresDescendingOrder()
    {
        var sounds = new List<SoundAsset>
        {
            MakeSound("Perfect", "impact", "boom", "fall", "crash"),
            MakeSound("Partial", "impact"),
            MakeSound("None", "music", "slow", "calm"),
        };

        _repo.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(sounds.AsReadOnly());

        var results = await _svc.GetTopCandidatesAsync("impact boom fall crash");

        for (var i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Score >= results[i].Score,
                $"Score at {i - 1} ({results[i - 1].Score}) should be >= score at {i} ({results[i].Score})");
        }
    }
}
