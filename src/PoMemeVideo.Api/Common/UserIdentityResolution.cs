using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// File-backed ANON id that survives both browser context changes and API restarts so a
    /// session created on one Playwright tab is still findable from another. Written only
    /// when the dev environment is the only thing running on the box (a guard keeps the
    /// persisted GUID from leaking across dev machines).
    /// </summary>
    internal static string DevAnonFilePath => Path.Combine(
        Path.GetTempPath(), "pomemevideo-dev-anon.txt");

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
        // Priority order:
        //   1. Real auth claims (OIDC / Guest sign-in) — never override an authenticated user.
        //   2. The on-disk ANON id — the canonical identity for this dev box. Survives browser
        //      context changes and API restarts. The cookie is just a per-context cache.
        //   3. The dev-anon cookie — only used when no file exists yet (i.e. first run after
        //      the file feature was added). Once the file exists, the cookie is a stale hint
        //      and ignored.
        //   4. Mint a fresh GUID and persist it.
        var existing = (UserId?)null;

        // Only honor NameIdentifier if it came from a *real* identity — not the dev ANON one
        // we minted on an earlier request and shoved into the principal via SignInAsync. The
        // "identity_type" claim distinguishes the two: ANON (dev), GUEST (guest login), OIDC.
        var identityType = httpContext.User.FindFirstValue("identity_type");
        var nameId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.Equals(identityType, "ANON", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(nameId, out var g))
        {
            existing = new UserId(g);
        }

        if (existing is null)
        {
            try
            {
                if (File.Exists(DevAnonFilePath))
                {
                    var onDisk = File.ReadAllText(DevAnonFilePath).Trim();
                    if (Guid.TryParse(onDisk, out var persisted))
                        existing = new UserId(persisted);
                }
            }
            catch
            {
                // Non-fatal — fall through to the cookie.
            }
        }

        if (existing is null)
            existing = TryGetDevAnonUserId(httpContext);

        // Debug: confirm what we're picking
        var dbg = httpContext.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("DevAnon");
        dbg?.LogInformation(
            "EnsureDevelopmentAnonymousIdentity: cookie={Cookie} file={File} identity={Identity} picked={Picked}",
            httpContext.Request.Cookies[DevAnonCookieName] ?? "(none)",
            File.Exists(DevAnonFilePath) ? File.ReadAllText(DevAnonFilePath).Trim() : "(none)",
            identityType ?? "(none)",
            existing?.Value.ToString() ?? "(new)");

        // 4. Last resort: mint a fresh GUID and persist it for the next process / browser.
        var fresh = existing is null;
        var userId = existing ?? new UserId(Guid.NewGuid());
        var gid = userId.Value;

        if (fresh)
        {
            try
            {
                File.WriteAllText(DevAnonFilePath, gid.ToString());
            }
            catch
            {
                // Non-fatal — without the file, sessions are still scoped to this process
                // and whatever browser contexts have the cookie. The next restart will mint
                // again, which is the same behaviour we had before this file existed.
            }
        }

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
        // are non-fatal — the dedicated cookie + on-disk id are what makes the dev experience work.
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
            // Auth-cookie signing is best-effort in dev. The dedicated cookie + on-disk id still
            // ensure a stable UserId across requests.
        }
    }
}
