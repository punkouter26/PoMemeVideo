// SOLID: Single Responsibility — ANON identity creation isolated from other auth concerns.
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;

namespace PoMemeVideo.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // ── GET /api/auth/me — return current signed-in user's identity ──────
        app.MapGet("/api/auth/me", (HttpContext httpContext) =>
        {
            var principal = httpContext.User;
            if (principal?.Identity?.IsAuthenticated != true)
                return Results.Ok(new AuthMeResponse(null, null));

            var displayName = principal.FindFirst(ClaimTypes.Name)?.Value
                           ?? principal.FindFirst("name")?.Value;
            var email = principal.FindFirst(ClaimTypes.Email)?.Value
                     ?? principal.FindFirst("preferred_username")?.Value;

            return Results.Ok(new AuthMeResponse(displayName, email));
        })
        .WithName("GetMe")
        .WithTags("Auth")
        .Produces<AuthMeResponse>(200)
        .AllowAnonymous();

        return app;
    }

    /// <remarks>
    /// Single Responsibility (SOLID): this endpoint family encapsulates only development guest identity issuance.
    /// </remarks>
    public static IEndpointRouteBuilder MapGuestAuthEndpoints(this IEndpointRouteBuilder app, IHostEnvironment env)
    {
        if (!env.IsDevelopment())
            return app;

        // ── POST /auth/guest — dev-only GUEST identity creation ───────────────
        static async Task<IResult> GuestLoginHandler(
            IUserIdentityRepository identityRepository,
            HttpContext httpContext,
            CancellationToken ct)
        {
            // Generate unique GUEST display name: GUEST + 6-digit suffix
            var suffix = Random.Shared.Next(100_000, 999_999);
            var displayName = $"GUEST{suffix}";

            var identity = new UserIdentity
            {
                IdentityType = "GUEST",
                DisplayName = displayName,
            };

            await identityRepository.CreateAsync(identity, ct);

            // Build claims principal for cookie sign-in
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, identity.IdentityId.ToString()),
                new(ClaimTypes.Name, displayName),
                new(ClaimTypes.Email, $"{displayName.ToLowerInvariant()}@guest.pomemevideo.local"),
                new("identity_type", "GUEST"),
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(claimsIdentity);

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
                });

            return Results.Ok(new
            {
                identityId = identity.IdentityId,
                displayName = identity.DisplayName,
                identityType = "GUEST",
            });
        }

        app.MapPost("/auth/guest", GuestLoginHandler)
        .WithName("GuestLogin")
        .WithTags("Auth")
        .Produces<object>(200)
        .AllowAnonymous();

        // Backward-compatible alias for existing tests/tools.
        app.MapPost("/auth/anon", GuestLoginHandler)
        .WithName("AnonLoginAlias")
        .WithTags("Auth")
        .Produces<object>(200)
        .AllowAnonymous();

        return app;
    }

    public static IEndpointRouteBuilder MapAnonAuthEndpoints(this IEndpointRouteBuilder app, IHostEnvironment env)
        => app.MapGuestAuthEndpoints(env);
}

internal sealed record AuthMeResponse(string? DisplayName, string? Email);
