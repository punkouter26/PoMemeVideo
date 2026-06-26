using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using PoMemeVideo.Api.Entities;
using PoMemeVideo.Api.Interfaces;

namespace PoMemeVideo.E2EAPI;

/// <summary>
/// E2EAPI — pure API-call emulation of the end-user journey, no browser.
/// Runs in the <c>Test</c> environment to prove the GUEST dev-bypass is available
/// for automated runs (see <c>MapGuestAuthEndpoints</c>).
/// </summary>
public sealed class AuthFlowApiTests : IAsyncLifetime
{
    private readonly IUserIdentityRepository _identityRepository = Substitute.For<IUserIdentityRepository>();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public Task InitializeAsync()
    {
        _identityRepository
            .CreateAsync(Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.ArgAt<UserIdentity>(0)));

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");
                builder.UseSetting("KeyVault:Uri", "");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IUserIdentityRepository>();
                    services.AddScoped<IUserIdentityRepository>(_ => _identityRepository);
                });
            });

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

    [Fact]
    public async Task Health_RespondsWithStatusDocument()
    {
        // In the Test env storage/AI are intentionally unconfigured, so a 503
        // "Degraded" is expected; the contract is that /health answers with a body.
        var response = await _client!.GetAsync("/health");
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"Unexpected /health status: {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("status", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuestLogin_ThenAuthMe_ReflectsIdentity()
    {
        var login = await _client!.PostAsync("/auth/guest", null);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await _client.GetFromJsonAsync<AuthMe>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Matches(@"^GUEST\d{8}$", me!.DisplayName);
    }

    private sealed record AuthMe(string? DisplayName, string? Email);
}
