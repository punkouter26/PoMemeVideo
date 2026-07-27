using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace PoMemeVideo.UnitTests.Auth;

/// <summary>
/// Guards the constitutional requirement that FakeAuthHandler can never authenticate a caller
/// in Production, where it would accept an identity asserted by an arbitrary request header.
/// </summary>
public sealed class FakeAuthHandlerTests
{
    private static FakeAuthHandler Create(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());

        return new FakeAuthHandler(
            options,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            environment);
    }

    [Fact]
    public void Constructor_InProduction_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Create(Environments.Production));

        Assert.Contains("Production", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void Constructor_OutsideProduction_Succeeds(string environmentName)
    {
        var handler = Create(environmentName);

        Assert.NotNull(handler);
    }
}
