using System.Net.Http.Headers;
using Microsoft.JSInterop;
using PoMemeVideo.Shared;

namespace PoMemeVideo.Client.Services;

/// <summary>
/// Stamps <see cref="CorrelationHeaders.SessionId"/> and <see cref="CorrelationHeaders.CorrelationId"/>
/// on every call the WASM client makes, so a browser action and its server-side log lines share ids.
/// </summary>
public sealed class CorrelationHeaderHandler : DelegatingHandler
{
    private readonly IJSRuntime _js;
    private string? _sessionId;

    public CorrelationHeaderHandler(IJSRuntime js) => _js = js;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // The BFF issues pmv-session-id as a non-HttpOnly cookie precisely so the client can echo
        // it back as a header. Cached after first read — it is stable for the browser session.
        _sessionId ??= await ReadSessionCookieAsync();

        if (!string.IsNullOrWhiteSpace(_sessionId))
            request.Headers.TryAddWithoutValidation(CorrelationHeaders.SessionId, _sessionId);

        // One correlation id per outbound call — this is the unit the server logs against.
        request.Headers.TryAddWithoutValidation(CorrelationHeaders.CorrelationId, Guid.NewGuid().ToString("N"));

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string?> ReadSessionCookieAsync()
    {
        try
        {
            var cookies = await _js.InvokeAsync<string>("poBrowser.readCookies");
            var match = cookies
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(c => c.StartsWith($"{CorrelationHeaders.SessionCookieName}=", StringComparison.Ordinal));

            return match?[(CorrelationHeaders.SessionCookieName.Length + 1)..];
        }
        catch (JSException)
        {
            // Prerender or a JS-disabled context — correlation degrades to server-side ids only.
            return null;
        }
    }
}
