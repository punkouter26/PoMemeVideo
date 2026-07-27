using System.Text.Json;

namespace PoMemeVideo.UnitTests.Domain;

/// <summary>
/// The strongly-typed ids were introduced over an existing database and an existing HTTP contract.
/// These tests pin the representation: anything other than a bare GUID string would silently
/// orphan every stored row (PartitionKey/RowKey) and break existing clients.
/// </summary>
public sealed class StronglyTypedIdTests
{
    private static readonly Guid Sample = Guid.Parse("11112222-3333-4444-5555-666677778888");

    [Fact]
    public void SessionId_SerialisesAsBareGuidString()
    {
        var json = JsonSerializer.Serialize(new SessionId(Sample));

        Assert.Equal($"\"{Sample}\"", json);
    }

    [Fact]
    public void SessionId_RoundTripsThroughJson()
    {
        var original = new SessionId(Sample);

        var restored = JsonSerializer.Deserialize<SessionId>(JsonSerializer.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Fact]
    public void AllIds_ToString_MatchesRawGuid_SoStorageKeysAreUnchanged()
    {
        // PartitionKey/RowKey are built via ToString() — these must stay identical to the
        // pre-refactor values or existing rows become unreachable.
        Assert.Equal(Sample.ToString(), new SessionId(Sample).ToString());
        Assert.Equal(Sample.ToString(), new UserId(Sample).ToString());
        Assert.Equal(Sample.ToString(), new SoundId(Sample).ToString());
        Assert.Equal(Sample.ToString(), new EntryId(Sample).ToString());
    }

    [Fact]
    public void SessionId_TryParse_AcceptsGuidString_SoRouteBindingWorks()
    {
        Assert.True(SessionId.TryParse(Sample.ToString(), null, out var parsed));
        Assert.Equal(new SessionId(Sample), parsed);
    }

    [Fact]
    public void SessionId_TryParse_RejectsNonGuid()
    {
        Assert.False(SessionId.TryParse("not-a-guid", null, out _));
    }

    [Fact]
    public void DistinctIdTypes_AreNotInterchangeable()
    {
        // The whole point: a SessionId and a UserId over the same Guid are different values,
        // so transposing repository arguments is now a compile error rather than a silent null.
        Assert.False(new SessionId(Sample).Equals((object)new UserId(Sample)));
    }
}
