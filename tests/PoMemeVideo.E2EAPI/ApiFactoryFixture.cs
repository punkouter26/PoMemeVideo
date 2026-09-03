using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace PoMemeVideo.E2EAPI;

/// <summary>
/// One <see cref="WebApplicationFactory{TEntryPoint}"/> for the whole E2EAPI suite.
/// <para>
/// Booting the host is the dominant cost here (well over a minute), and two factories running
/// concurrently interfere: <c>Program</c> configures process-wide state — Serilog's static
/// <c>Log.Logger</c> among it — so a second host starting mid-run disturbs the first, which is
/// how a health-check assertion that passes in isolation fails in a full run.
/// </para>
/// </summary>
public sealed class ApiFactoryFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    public IUserIdentityRepository IdentityRepository { get; } = Substitute.For<IUserIdentityRepository>();

    public Task InitializeAsync()
    {
        IdentityRepository
            .CreateAsync(Arg.Any<UserIdentity>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.ArgAt<UserIdentity>(0)));

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");
                builder.UseSetting("KeyVault:Uri", "");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IUserIdentityRepository>();
                    services.AddScoped<IUserIdentityRepository>(_ => IdentityRepository);
                });
            });

        return Task.CompletedTask;
    }

    /// <summary>A fresh cookie jar per test, over the shared host.</summary>
    public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    public async Task DisposeAsync() => await Factory.DisposeAsync();
}

/// <summary>
/// Every E2EAPI class joins this collection, so they run serially against the shared host
/// above rather than each standing up their own.
/// </summary>
[CollectionDefinition("E2EAPI")]
public sealed class ApiCollection : ICollectionFixture<ApiFactoryFixture>
{
}
