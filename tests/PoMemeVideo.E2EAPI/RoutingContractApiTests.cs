using System.Net;
using System.Net.Http.Json;

namespace PoMemeVideo.E2EAPI;

/// <summary>
/// Pins the routes and authorization posture the Blazor client depends on.
/// <para>
/// This suite exists because of a live bug: the Reveal page built
/// <c>/api/output/sessions/{id}/download</c>, a route that has never existed — the real one is
/// <c>/download/video</c>. The URL was interpolated into an <c>eval</c>'d JavaScript string, so
/// nothing failed at compile time.
/// </para>
/// <para>
/// The symptom was worse than a 404. Unmatched paths fall through to <c>MapFallbackToFile</c>,
/// the WASM shell, so the request answered <b>200 with index.html</b> and the browser saved the
/// HTML page under the name <c>pomemevideo_&lt;id&gt;.mp4</c>. That is also why these tests assert
/// on the response's <i>shape</i> rather than on a 404 status: in a SPA-hosting app, a removed
/// API route does not 404.
/// </para>
/// </summary>
[Collection("E2EAPI")]
public sealed class RoutingContractApiTests
{
    private readonly HttpClient _client;

    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public RoutingContractApiTests(ApiFactoryFixture fixture) => _client = fixture.CreateClient();

    /// <summary>A route that exists answers something other than a bodyless framework 404.</summary>
    private static void AssertRouteExists(HttpResponseMessage response, string route)
        => Assert.False(
            response.StatusCode == System.Net.HttpStatusCode.NotFound
            && response.Content.Headers.ContentType?.MediaType != "application/json",
            $"Route not mapped: {route}");

    private async Task SignInAsGuestAsync()
    {
        var login = await _client.PostAsync("/auth/guest", null);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ── Routes the client actually calls ──────────────────────────────────

    [Theory]
    [InlineData("/api/output/sessions/{id}/download/video")]
    [InlineData("/api/output/sessions/{id}/stream/video")]
    [InlineData("/api/output/sessions/{id}/stream/source")]
    [InlineData("/api/output/sessions/{id}/script")]
    [InlineData("/api/output/sessions/{id}/export/gif")]
    [InlineData("/api/output/sessions/{id}/export/punchline")]
    public async Task OutputRoutesTheClientCalls_AreMapped(string template)
    {
        await SignInAsGuestAsync();
        var route = template.Replace("{id}", SessionId.ToString());

        var response = await _client.GetAsync(route);

        // A missing session yields a JSON 404 body from the handler; an unmapped route yields
        // the framework's bodyless 404. Only the latter is a routing failure.
        AssertRouteExists(response, route);
    }

    [Fact]
    public async Task BareDownloadRoute_ServesTheShellNotAVideo()
    {
        // The exact URL the Reveal page used to build. It answers 200 — from the SPA fallback —
        // so the only thing distinguishing it from the real route is the content type. This is
        // the assertion that would have caught the original bug.
        await SignInAsGuestAsync();

        var response = await _client.GetAsync($"/api/output/sessions/{SessionId}/download");
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        Assert.NotEqual("video/mp4", mediaType);
        Assert.Equal("text/html", mediaType);
    }

    [Fact]
    public async Task RealDownloadRoute_IsHandledByTheApiNotTheShell()
    {
        // The corrected route reaches the Output handler, which 404s a session that does not
        // exist — with a JSON body, unlike the shell.
        await SignInAsGuestAsync();

        var response = await _client.GetAsync($"/api/output/sessions/{SessionId}/download/video");

        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task IngestionSessionsList_IsMapped()
    {
        await SignInAsGuestAsync();

        var response = await _client.GetAsync("/api/ingestion/sessions");

        AssertRouteExists(response, "/api/ingestion/sessions");
    }

    // ── Removed routes stay removed ───────────────────────────────────────

    [Fact]
    public async Task RemovedAnonLoginAlias_NoLongerSucceeds()
    {
        // It used to answer 200 with a GUEST identity document.
        var response = await _client.PostAsync("/auth/anon", null);

        Assert.False(response.IsSuccessStatusCode, $"/auth/anon still answers {(int)response.StatusCode}");
    }

    [Fact]
    public async Task RemovedDevSessionReset_NoLongerSucceeds()
    {
        var response = await _client.DeleteAsync("/api/dev/session");

        Assert.False(response.IsSuccessStatusCode, $"/api/dev/session still answers {(int)response.StatusCode}");
    }

    [Fact]
    public async Task RemovedModelDownloadTrigger_NoLongerSucceeds()
    {
        // This one shelled out to `python download-models.py` from an AllowAnonymous endpoint.
        var response = await _client.PostAsync("/api/config/ai-model/download", null);

        Assert.False(response.IsSuccessStatusCode, $"model-download trigger still answers {(int)response.StatusCode}");
    }

    [Fact]
    public async Task RemovedSessionOptionsRoute_NoLongerSucceeds()
    {
        await SignInAsGuestAsync();

        var response = await _client.PutAsJsonAsync(
            $"/api/ingestion/sessions/{SessionId}/options", new { aggressiveVisuals = true });

        Assert.False(response.IsSuccessStatusCode, $"session options still answers {(int)response.StatusCode}");
    }

    [Fact]
    public async Task BrowserDirectorCallback_IsMappedAndAnonymous()
    {
        // The browser posts its locally-computed script here without waiting on the auth cookie.
        // With no inference pending for this session the handler answers a JSON 404 — which is
        // the handler running, not the route being absent.
        var response = await _client.PostAsJsonAsync(
            $"/api/processing/sessions/{SessionId}/browser-director-result", new { entries = Array.Empty<object>() });

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ── Public surface ────────────────────────────────────────────────────

    [Fact]
    public async Task Config_IsReachableAnonymously()
    {
        var response = await _client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthLive_IsReachableAnonymously()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AiModel_ReportsOnlyTheSupportedProviders()
    {
        var model = await _client.GetFromJsonAsync<AiModelPayload>("/api/config/ai-model");

        Assert.NotNull(model);
        Assert.Contains(model!.Provider, RuntimeAiSettings.ValidProviders);
    }

    [Fact]
    public async Task AiModel_RejectsARemovedProvider()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/config/ai-model", new { provider = "Ollama" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AiModel_RejectsBrowserLlmWhenNoWeightsAreInstalled()
    {
        // BrowserLLM is a valid provider, but selecting it without the ONNX weights on disk
        // would leave the engine awaiting an inference that can never complete.
        var response = await _client.PutAsJsonAsync(
            "/api/config/ai-model", new { provider = "BrowserLLM", browserLLMModel = "smollm2-360m-instruct-onnx" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AiModel_AcceptsAiFoundryAndEchoesTheDeployment()
    {
        var response = await _client.PutAsJsonAsync(
            "/api/config/ai-model", new { provider = "AiFoundry", aiFoundryDeployment = "gpt-4o-mini" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AiModelPayload>();
        Assert.Equal("AiFoundry", payload!.Provider);
        Assert.Equal("gpt-4o-mini", payload.AiFoundryDeployment);
    }

    // Deny-by-default is deliberately NOT asserted here. In the Test environment these
    // endpoints reach unconfigured storage and answer 500 after the resilience pipeline
    // exhausts its retries — which says nothing about the authorization decision. CLAUDE.md
    // is explicit that authorization changes are verified under Staging, not under a
    // Development/Test host where the auth stack is deliberately relaxed.

    private sealed record AiModelPayload(string Provider, string? AiFoundryDeployment);
}
