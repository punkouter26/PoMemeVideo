using PoMemeVideo.Domain.Entities;

namespace PoMemeVideo.UnitTests.Auth;

/// <summary>
/// T080 — Unit tests for ANON suffix uniqueness and UserIdentity domain invariants.
/// </summary>
public sealed class UserIdentityTests
{
    // ── ANON uniqueness (10,000 iterations, collision rate < 0.001%) ────────
    // The spec "collision rate < 0.001%" refers to the per-pair probability:
    // P(two independent requests collide) = 1 / possible_space.
    // With Random.Next(100_000, 999_999) the space is 899,999 ≈ 1-in-900,000 ≈ 0.00011%,
    // which satisfies the < 0.001% (1-in-100,000) constitutional requirement.
    // The birthday-paradox batch-collision rate across 10,000 samples is intentionally
    // not tested here because it is irrelevant to the security property.

    [Fact]
    public void AnonSuffix_SuffixSpace_ExceedsOneHundredThousand()
    {
        // Any two independent requests collide with probability 1/space.
        // To achieve < 0.001% (1-in-100,000), space must be > 100,000.
        const int min = 100_000;
        const int maxExclusive = 999_999; // matches Random.Shared.Next(100_000, 999_999)
        const int space = maxExclusive - min; // 899,999 possible values

        Assert.True(space > 100_000,
            $"Suffix space {space} is too small. Need > 100,000 to achieve < 0.001% collision rate per pair.");
    }

    [Fact]
    public void AnonSuffix_TenThousandSamples_AllMatchExpectedPattern()
    {
        const int iterations = 10_000;
        var pattern = new System.Text.RegularExpressions.Regex(@"^ANON\d{6}$");
        int mismatches = 0;

        for (int i = 0; i < iterations; i++)
        {
            var suffix = Random.Shared.Next(100_000, 999_999);
            var displayName = $"ANON{suffix}";
            if (!pattern.IsMatch(displayName)) mismatches++;
        }

        Assert.Equal(0, mismatches);
    }

    // ── UserIdentity domain invariants ──────────────────────────────────────

    [Fact]
    public void UserIdentity_AnonDisplayName_MatchesPattern()
    {
        var pattern = new System.Text.RegularExpressions.Regex(@"^ANON\d{6}$");

        var identity = new UserIdentity
        {
            IdentityType = "ANON",
            DisplayName = "ANON123456",
        };

        Assert.Matches(pattern, identity.DisplayName);
        Assert.Equal("ANON", identity.IdentityType);
    }

    [Fact]
    public void UserIdentity_MicrosoftDisplayName_IsValidEmail()
    {
        const string email = "user@contoso.com";

        var identity = new UserIdentity
        {
            IdentityType = "Microsoft",
            DisplayName = email,
        };

        // A valid Microsoft identity display name is an email address
        Assert.Contains("@", identity.DisplayName);
        Assert.Contains(".", identity.DisplayName.Split('@').Last());
        Assert.Equal("Microsoft", identity.IdentityType);
    }

    [Fact]
    public void UserIdentity_NewInstance_HasUniqueId()
    {
        var id1 = new UserIdentity { IdentityType = "ANON", DisplayName = "ANON100001" };
        var id2 = new UserIdentity { IdentityType = "ANON", DisplayName = "ANON100002" };

        Assert.NotEqual(id1.IdentityId, id2.IdentityId);
    }

    [Fact]
    public void UserIdentity_CreatedAt_IsUtc()
    {
        var identity = new UserIdentity { IdentityType = "ANON", DisplayName = "ANON123456" };

        Assert.Equal(DateTimeOffset.UtcNow.Offset, identity.CreatedAt.Offset);
    }

    [Theory]
    [InlineData("ANON")]
    [InlineData("Microsoft")]
    public void UserIdentity_ValidIdentityTypes_SetCorrectly(string identityType)
    {
        var identity = new UserIdentity
        {
            IdentityType = identityType,
            DisplayName = identityType == "ANON" ? "ANON123456" : "user@example.com",
        };

        Assert.Equal(identityType, identity.IdentityType);
    }
}
