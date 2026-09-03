using System.Net;
using System.Net.Http.Json;

namespace PoMemeVideo.E2EAPI;

/// <summary>
/// E2EAPI — pure API-call emulation of the end-user journey, no browser.
/// Runs in the <c>Test</c> environment to prove the GUEST dev-bypass is available
/// for automated runs (see <c>MapGuestAuthEndpoints</c>).
/// </summary>
[Collection("E2EAPI")]
public sealed class AuthFlowApiTests
{
    private readonly HttpClient _client;

    public AuthFlowApiTests(ApiFactoryFixture fixture) => _client = fixture.CreateClient();

    [Fact]
    public async Task Health_RespondsWithStatusDocument()
    {
        // In the Test env storage/AI are intentionally unconfigured, so a 503
        // "Degraded" is expected; the contract is that /health answers with a body.
        var response = await _client.GetAsync("/health");
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"Unexpected /health status: {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("status", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuestLogin_ThenAuthMe_ReflectsIdentity()
    {
        var login = await _client.PostAsync("/auth/guest", null);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await _client.GetFromJsonAsync<AuthMe>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Matches(@"^GUEST\d{8}$", me!.DisplayName);
    }

    private sealed record AuthMe(string? DisplayName, string? Email);
}
