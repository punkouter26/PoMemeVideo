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

        // Reuse an existing NameIdentifier claim if the auth cookie principal already carries one
        // (e.g. when Cookie auth couldn't decode the cookie at this layer, or when the request
        // pipe is missing the cookie header). Generating Guid.NewGuid() here was producing a fresh
        // userId per request, which made VideoSessionRepository.GetByIdAsync(partitionKey=userId)
        // fail for sessions stored under a previous ANON GUID.
        var existingNameId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userIdStr = Guid.TryParse(existingNameId, out var existingGuid)
            ? existingGuid.ToString()
            : Guid.NewGuid().ToString();

        // Stable display name keyed to the userId (so the same ANON# gets shown across requests
        // for the same user, instead of a random number rotating every call).
        var gid = Guid.Parse(userIdStr);
        var anonNumber = unchecked((int)(gid.GetHashCode() & 0x7FFFFFFF) % 900_000 + 100_000);
        var displayName = $"ANON{anonNumber}";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userIdStr),
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
