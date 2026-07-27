// Rule 5 (AI Test Interception): in test environments every Azure AI call is answered locally so
// a CI run can exercise the real code path — SDK client, serialisation, retry policy — without
// spending a single token against a live deployment.
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Azure.AI.OpenAI;

namespace PoMemeVideo.Api.Common;

/// <summary>
/// Short-circuits outbound Azure OpenAI chat-completion requests with a deterministic canned
/// response. Installed only when <see cref="AiInterception.IsEnabled"/> is true.
/// </summary>
internal sealed class AiInterceptionHandler : DelegatingHandler
{
    /// <summary>Shape-compatible with a chat-completions response; content is an empty JSON array.</summary>
    private const string CannedCompletion = """
        {
          "id": "chatcmpl-intercepted",
          "object": "chat.completion",
          "created": 0,
          "model": "intercepted",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "[]" },
              "finish_reason": "stop"
            }
          ],
          "usage": { "prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0 }
        }
        """;

    public AiInterceptionHandler() : base(new HttpClientHandler()) { }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(CannedCompletion, Encoding.UTF8, "application/json"),
        };

        // Marker so a test can assert the call never left the process.
        response.Headers.TryAddWithoutValidation("X-PoMemeVideo-Intercepted", "true");

        return Task.FromResult(response);
    }
}

/// <summary>
/// Decides whether AI calls are intercepted, and builds the SDK options that enforce it.
/// </summary>
public static class AiInterception
{
    /// <summary>
    /// True in Test environments, or whenever <c>UseMockAI</c> is set. Never true in Production —
    /// an intercepted production deployment would silently return empty director scripts.
    /// </summary>
    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        if (environment.IsProduction())
            return false;

        return environment.IsEnvironment("Test")
            || configuration.GetValue<bool>("UseMockAI");
    }

    /// <summary>
    /// Returns SDK options routed through <see cref="AiInterceptionHandler"/> when interception is
    /// on, otherwise <c>null</c> so the SDK uses its own default transport.
    /// </summary>
    public static AzureOpenAIClientOptions? BuildClientOptions(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (!IsEnabled(environment, configuration))
            return null;

        return new AzureOpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(new AiInterceptionHandler())),
        };
    }
}
