using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace PoMemeVideo.IntegrationTests.Auth;

/// <summary>
/// T081 — Integration tests for POST /auth/guest.
/// Verifies: response sets session cookie, displayName matches GUEST\d{8}, UserIdentity persisted.
/// </summary>
[Collection("Integration")]
public sealed class GuestAuthTests : IAsyncLifetime
{
    private readonly IUserIdentityRepository _identityRepository = Substitute.For<IUserIdentityRepository>();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public Task InitializeAsync()
    {
        // Repository returns the identity it receives (identity pass-through)
        _identityRepository
            .CreateAsync(Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.ArgAt<UserIdentity>(0)));

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.UseSetting("KeyVault:Uri", ""); // skip KV in CI/test
                builder.UseSetting("SkipAutoSeed", "true");

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IUserIdentityRepository>();
                    services.AddScoped<IUserIdentityRepository>(_ => _identityRepository);
                });
            });

        // UseCookies so the HttpClient handles Set-Cookie headers
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }

    // ── POST /auth/guest ──────────────────────────────────────────────────────

    [Fact]
    public async Task PostAnonLogin_Returns200WithDisplayName()
    {
        var response = await _client!.PostAsync("/auth/guest", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GuestLoginResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.IdentityId);
        Assert.Matches(@"^GUEST\d{8}$", body.DisplayName);
        Assert.Equal("GUEST", body.IdentityType);
    }

    [Fact]
    public async Task PostAnonLogin_SetsSessionCookie()
    {
        var response = await _client!.PostAsync("/auth/guest", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Response should contain a Set-Cookie header for the auth cookie
        var hasCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
                        cookies!.Any(c => c.Contains(".AspNetCore.Cookies") || c.Contains("Cookies"));
        Assert.True(hasCookie, "Expected Set-Cookie header with authentication cookie.");
    }

    [Fact]
    public async Task PostAnonLogin_PersistsUserIdentityToRepository()
    {
        await _client!.PostAsync("/auth/guest", null);

        // Verify repository CreateAsync was called exactly once with a GUEST identity
        await _identityRepository
            .Received(1)
            .CreateAsync(
                Arg.Is<UserIdentity>(u =>
                    u.IdentityType == "GUEST" &&
                    Regex.IsMatch(u.DisplayName, @"^GUEST\d{8}$")),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostAnonLogin_MultipleCalls_ProduceDifferentDisplayNames()
    {
        var response1 = await _client!.PostAsync("/auth/guest", null);
        var response2 = await _client!.PostAsync("/auth/guest", null);

        var body1 = await response1.Content.ReadFromJsonAsync<GuestLoginResponse>();
        var body2 = await response2.Content.ReadFromJsonAsync<GuestLoginResponse>();

        // Extremely unlikely to collide with 90,000,000 possible suffixes
        Assert.NotEqual(body1?.DisplayName, body2?.DisplayName);
    }

    [Fact]
    public async Task GetAuthMe_AfterAnonLogin_ReturnsDisplayName()
    {
        // Login first
        await _client!.PostAsync("/auth/guest", null);

        // /api/auth/me should now reflect the signed-in identity
        var me = await _client.GetFromJsonAsync<AuthMeResponse>("/api/auth/me");

        Assert.NotNull(me);
        Assert.NotNull(me.DisplayName);
        Assert.Matches(@"^GUEST\d{8}$", me.DisplayName);
    }

    private sealed record GuestLoginResponse(Guid IdentityId, string DisplayName, string IdentityType);
    private sealed record AuthMeResponse(string? DisplayName, string? Email);
}
