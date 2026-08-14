using Microsoft.Playwright;

namespace PoMemeVideo.E2EUI;

/// <summary>
/// E2EUI — C# Playwright browser tests. Driven against a running instance via the
/// <c>E2E_BASE_URL</c> environment variable (e.g. http://localhost:7000). When it is
/// not set the suite no-ops so build/CI stay green without a live server or browsers.
/// Provision browsers once with: <c>pwsh bin/Debug/net10.0/playwright.ps1 install</c>.
/// </summary>
public sealed class LoginPageUiTests
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("E2E_BASE_URL");

    [Fact]
    public async Task UnauthenticatedUser_IsForcedToLogin()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
            return; // No target configured — skip in headless build/CI.

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync();

        await page.GotoAsync(BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        // The auth wall redirects anonymous users to /login. The trailing ** is load-bearing:
        // the redirect carries ?returnUrl=..., and a bare "**/login" fails to match that, timing
        // out on a redirect that actually happened.
        await page.WaitForURLAsync("**/login**", new PageWaitForURLOptions { Timeout = 15_000 });

        // Reaching /login only means the server issued the redirect — the sign-in UI is rendered
        // by Blazor WASM afterwards. Reading body text right away races that boot and returns the
        // shell's inlined CSS, so wait for the button itself before asserting.
        await page.WaitForSelectorAsync(
            "text=SIGN IN WITH MICROSOFT",
            new PageWaitForSelectorOptions { Timeout = 30_000 });

        var body = await page.TextContentAsync("body");
        Assert.Contains("MICROSOFT", body ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
