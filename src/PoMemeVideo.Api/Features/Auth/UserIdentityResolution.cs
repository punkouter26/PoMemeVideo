using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace PoMemeVideo.Api.Features.Auth;

internal static class UserIdentityResolution
{
    // Entra ID's immutable per-user object identifier. This is a Guid, unlike the
    // `sub`/NameIdentifier claim which is a non-parseable pairwise subject id.
    private const string EntraObjectIdSchemaClaim =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public static Guid? TryGetUserId(HttpContext httpContext)
    {
        var user = httpContext.User;

        // Cookie-based GUEST/ANON identities set NameIdentifier to a Guid directly.
        var nameId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(nameId, out var userId))
            return userId;

        // Entra ID (OIDC) users: NameIdentifier is the `sub` pairwise id (not a Guid),
        // so fall back to the object-identifier (`oid`) claim, which is a stable Guid.
        var oid = user.FindFirstValue(EntraObjectIdSchemaClaim)
                  ?? user.FindFirstValue("oid");
        return Guid.TryParse(oid, out var objectId) ? objectId : null;
    }

    public static async Task EnsureDevelopmentAnonymousIdentityAsync(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
            return;

        var displayName = $"ANON{Random.Shared.Next(100_000, 1_000_000)}";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, $"{displayName.ToLowerInvariant()}@anon.pomemevideo.local"),
            new("identity_type", "ANON"),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        httpContext.User = principal;

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
            });
    }
}
