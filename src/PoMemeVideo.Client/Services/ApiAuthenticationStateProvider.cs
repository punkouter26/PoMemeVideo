using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace PoMemeVideo.Client.Services;

/// <summary>
/// Bridges the BFF cookie session into Blazor's auth pipeline by querying the
/// server-owned <c>/api/auth/me</c> endpoint. The WASM client never sees a token —
/// it only learns whether the HttpOnly session cookie maps to a signed-in user.
/// </summary>
public sealed class ApiAuthenticationStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var me = await http.GetFromJsonAsync<AuthMe>("api/auth/me");
            if (me is null || (string.IsNullOrWhiteSpace(me.DisplayName) && string.IsNullOrWhiteSpace(me.Email)))
                return Anonymous;

            Claim[] claims =
            [
                new(ClaimTypes.Name, me.DisplayName ?? me.Email ?? "user"),
                new(ClaimTypes.Email, me.Email ?? ""),
            ];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "BffCookie"));
            return new AuthenticationState(principal);
        }
        catch
        {
            return Anonymous;
        }
    }

    /// <summary>Force a re-query after login/logout so the UI re-evaluates authorization.</summary>
    public void NotifyAuthenticationStateChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private sealed record AuthMe(string? DisplayName, string? Email);
}
