using PoMemeVideo.Shared;

namespace PoMemeVideo.Api.Common;

/// <summary>
/// Copies the inbound session/correlation identifiers onto every outbound HTTP call so downstream
/// services (Ollama, AI Foundry, Azure OpenAI) log under the same ids as the originating request.
/// </summary>
internal sealed class CorrelationPropagationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationPropagationHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is not null)
        {
            Propagate(request, CorrelationHeaders.SessionId, ResolveSessionId(context));
            Propagate(request, CorrelationHeaders.CorrelationId, ResolveCorrelationId(context));
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>Inbound header wins; otherwise fall back to the BFF session cookie.</summary>
    public static string? ResolveSessionId(HttpContext context)
        => context.Request.Headers[CorrelationHeaders.SessionId].FirstOrDefault()
           ?? context.Request.Cookies[CorrelationHeaders.SessionCookieName];

    /// <summary>Inbound header wins; otherwise use the per-request trace identifier.</summary>
    public static string ResolveCorrelationId(HttpContext context)
        => context.Request.Headers[CorrelationHeaders.CorrelationId].FirstOrDefault()
           ?? context.TraceIdentifier;

    private static void Propagate(HttpRequestMessage request, string header, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || request.Headers.Contains(header))
            return;

        request.Headers.TryAddWithoutValidation(header, value);
    }
}
