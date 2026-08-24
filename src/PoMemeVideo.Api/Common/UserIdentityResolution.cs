using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PoMemeVideo.Shared.Domain;

// Lives in Common, not the Auth slice: every slice needs "who is calling?" and a
// slice must not take a dependency on a sibling to answer it.
namespace PoMemeVideo.Api.Common;

public static class UserIdentityResolution
{
    // Entra ID's immutable per-user object identifier. This is a Guid, unlike the
    // `sub`/NameIdentifier claim which is a non-parseable pairwise subject id.
    private const string EntraObjectIdSchemaClaim =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>
    /// Dedicated dev-mode cookie that carries the ANON userId independently of the auth
    /// pipeline. The cookie auth scheme's roundtrip is not reliable when OIDC is configured
    /// (AddMicrosoftIdentityWebApp registers its own scheme), so we don't lean on User.Claims
    /// across requests — we read/write this cookie directly.
    /// </summary>
    internal const string DevAnonCookieName = "PmvDevAnon";

    public static UserId? TryGetUserId(HttpContext httpContext)
    {
        var user = httpContext.User;

        // Cookie-based GUEST/ANON identities set NameIdentifier to a Guid directly.
        var nameId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(nameId, out var userId))
            return new UserId(userId);

        // Entra ID (OIDC) users: NameIdentifier is the `sub` pairwise id (not a Guid),
        // so fall back to the object-identifier (`oid`) claim, which is a stable Guid.
        var oid = user.FindFirstValue(EntraObjectIdSchemaClaim)
                  ?? user.FindFirstValue("oid");
        return Guid.TryParse(oid, out var objectId) ? new UserId(objectId) : null;
    }

    /// <summary>
    /// Reads the ANON userId directly from <see cref="DevAnonCookieName"/>. Returns null
    /// when the cookie is missing, empty, or unparseable. Bypasses the auth pipeline entirely
    /// so the value is stable across requests even when Cookie auth + OIDC registration don't
    /// roundtrip the same way.
    /// </summary>
    public static UserId? TryGetDevAnonUserId(HttpContext httpContext)
    {
        var raw = httpContext.Request.Cookies[DevAnonCookieName];
        return Guid.TryParse(raw, out var g) ? new UserId(g) : null;
    }

    public static async Task EnsureDevelopmentAnonymousIdentityAsync(HttpContext httpContext)
    {
        // 1. Trust the dedicated dev-anon cookie first. If the browser already has one, reuse it
        //    verbatim — the same identity across requests keeps VideoSessions / sound library
        //    scoped lookups working.
        var existing = TryGetDevAnonUserId(httpContext);

        // 2. Then trust whatever Claims auth scheme handed us — covers the case where the
        //    browser arrived with a real auth cookie (Guest or OIDC) and the dev ANON
        //    middleware shouldn't replace it.
        if (existing is null)
        {
            var nameId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(nameId, out var g))
                existing = new UserId(g);
        }

        // 3. Mint a fresh GUID and persist it in our own cookie.
        if (existing is null)
            existing = new UserId(Guid.NewGuid());

        var userId = existing.Value;
        var gid = userId.Value;
        var anonNumber = unchecked((int)(gid.GetHashCode() & 0x7FFFFFFF) % 900_000 + 100_000);
        var displayName = $"ANON{anonNumber}";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, gid.ToString()),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, $"{displayName.ToLowerInvariant()}@anon.pomemevideo.local"),
            new("identity_type", "ANON"),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        httpContext.User = principal;

        // Set the dedicated cookie directly so subsequent requests reuse the same userId
        // regardless of whether the auth scheme's cookie roundtrip succeeds.
        httpContext.Response.Cookies.Append(
            DevAnonCookieName,
            gid.ToString(),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30),
            });

        // Also drive the auth-cookie sign-in so any [Authorize] check passes. Failures here
        // are non-fatal — the dedicated cookie is what makes the dev experience work.
        try
        {
            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
                });
        }
        catch
        {
            // Auth-cookie signing is best-effort in dev. The dedicated cookie still ensures a
            // stable UserId across requests.
        }
    }
}
