// Dev/Test authentication driven entirely by request headers, so integration and E2E suites can
// assume any identity without touching Entra ID or the cookie pipeline.
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PoMemeVideo.Api.Features.Auth;

/// <summary>
/// Authenticates a request from the <c>X-Fake-User</c> / <c>X-Fake-Roles</c> headers.
/// Registered only outside Production; the constructor throws if that ever stops being true.
/// </summary>
internal sealed class FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Fake";
    public const string UserHeader = "X-Fake-User";
    public const string RolesHeader = "X-Fake-Roles";

    public FakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHostEnvironment environment)
        : base(options, logger, encoder)
    {
        // Guardrail: a misconfigured Production host must fail fast and loudly rather than
        // silently accept an identity asserted by an arbitrary request header.
        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                $"{nameof(FakeAuthHandler)} must never be initialized in a Production environment. " +
                "It authenticates callers from unverified request headers.");
        }
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var userName = userValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userName))
            return Task.FromResult(AuthenticateResult.Fail($"{UserHeader} was present but empty."));

        // Stable userId derived from the name so repeated requests for the same fake user resolve
        // to the same partition key — repositories are keyed on this Guid.
        var userId = DeterministicUserId(userName);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Email, $"{userName.ToLowerInvariant()}@fake.pomemevideo.local"),
            new("identity_type", "FAKE"),
        };

        if (Request.Headers.TryGetValue(RolesHeader, out var roleValues))
        {
            var roles = roleValues
                .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    /// <summary>Maps a fake user name onto a stable Guid (MD5 of the name, used purely as a hash).</summary>
    private static Guid DeterministicUserId(string userName)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(userName.ToLowerInvariant()));
        return new Guid(bytes);
    }
}
