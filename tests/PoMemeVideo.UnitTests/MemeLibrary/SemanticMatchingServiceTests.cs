using NSubstitute;

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
        SoundId = SoundId.New(),
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

    // ── Natural-language captions ─────────────────────────────────────────
    // Vision labels are captions, not tags. Matching on the tag vocabulary alone returned an
    // empty list for every caption in a real run, which left the director with only its
    // fallback sounds. These pin the behaviour that fixed it.

    [Fact]
    public async Task GetTopCandidatesAsync_NaturalLanguageCaption_StillFindsCandidates()
    {
        var sounds = new List<SoundAsset>
        {
            MakeSound("Coffin Dance Meme", "funny", "reaction"),
            MakeSound("Smoke Detector Beep", "funny", "reaction"),
            MakeSound("Sad Violin", "sad", "slow"),
        };
        _repo.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(sounds.AsReadOnly());

        // Not one word of this is a tag — it only matches through the display name.
        var results = await _svc.GetTopCandidatesAsync("man posing mid-dance with raised fist");

        Assert.NotEmpty(results);
        Assert.Equal("Coffin Dance Meme", results[0].Sound.DisplayName);
    }

    [Fact]
    public async Task GetTopCandidatesAsync_CaptionSharingNoTerm_ReturnsEmpty()
    {
        var sounds = new List<SoundAsset> { MakeSound("Vine Boom", "impact") };
        _repo.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(sounds.AsReadOnly());

        // No shared term is a genuine "no signal" answer — the caller's fallback handles it.
        Assert.Empty(await _svc.GetTopCandidatesAsync("underwater basket weaving"));
    }

    [Fact]
    public async Task GetTopCandidatesAsync_RareTermOutranksUbiquitousOne()
    {
        // "funny" is on every sound and so carries no information; "trombone" is on one.
        var sounds = new List<SoundAsset>
        {
            MakeSound("Sad Trombone", "funny"),
            MakeSound("Clip One", "funny"),
            MakeSound("Clip Two", "funny"),
            MakeSound("Clip Three", "funny"),
        };
        _repo.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(sounds.AsReadOnly());

        var results = await _svc.GetTopCandidatesAsync("funny trombone");

        Assert.Equal("Sad Trombone", results[0].Sound.DisplayName);
    }

    [Fact]
    public async Task GetTopCandidatesAsync_UbiquitousTermIsStillMatchable()
    {
        // Guards the smoothing: unsmoothed idf zeroes a term carried by every sound, which
        // would make the entire library unreachable for that query.
        var sounds = new List<SoundAsset> { MakeSound("Alpha", "boom"), MakeSound("Beta", "boom") };
        _repo.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(sounds.AsReadOnly());

        Assert.Equal(2, (await _svc.GetTopCandidatesAsync("boom", topN: 10)).Count);
    }

    [Fact]
    public async Task GetTopCandidatesAsync_TiedScores_AreOrderedDeterministically()
    {
        // Equal scores previously fell back to storage order, so the same few sounds always
        // won and the rest of a tie group was unreachable.
        var sounds = new List<SoundAsset>
        {
            MakeSound("Tie One", "boom", "impact"),
            MakeSound("Tie Two", "boom", "impact"),
            MakeSound("Tie Three", "boom", "impact"),
        };
        _repo.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(sounds.AsReadOnly());

        var first = await _svc.GetTopCandidatesAsync("boom impact", topN: 3);
        var second = await new SemanticMatchingService(_repo).GetTopCandidatesAsync("boom impact", topN: 3);

        Assert.Equal(
            first.Select(r => r.Sound.SoundId),
            second.Select(r => r.Sound.SoundId));
        Assert.Equal(first.Select(r => r.Sound.SoundId).OrderBy(id => id.Value), first.Select(r => r.Sound.SoundId));
    }
}
