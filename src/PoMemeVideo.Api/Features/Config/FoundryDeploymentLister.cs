// SOLID: Single Responsibility — enumerate AI Foundry / Azure OpenAI deployments from the
// Azure Resource Manager REST API and surface a curated, dropdown-friendly list.
//
// Why a hand-rolled ARM client instead of Azure.ResourceManager?
//   - The AI Foundry account we're querying is `po-aiservices-shared` in `PoShared` (a
//     cross-subscription shared resource). It exposes a tiny, stable REST surface and the
//     Azure.AI.OpenAI SDK does not include deployment enumeration at all — only chat
//     completions. Pulling in Azure.ResourceManager + dependencies is overkill for one
//     GET that returns JSON.
//   - We authenticate with the same credential pattern the rest of the app uses for
//     Azure endpoints: API key (preferred when present) or DefaultAzureCredential.
//
// The result is cached in-memory for 5 minutes to keep the UI dropdown snappy and to
// avoid hammering ARM (which is throttled harder than the data plane).
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PoMemeVideo.Api.Features.Config;

/// <summary>
/// Lists AI Foundry / Azure OpenAI deployments so the UI can offer them in a dropdown.
/// </summary>
public sealed class FoundryDeploymentLister
{
    private const string ApiVersion = "2023-05-01";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<FoundryDeploymentLister> _logger;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public FoundryDeploymentLister(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<FoundryDeploymentLister> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Returns the deployments for the configured AI Foundry / Azure OpenAI account,
    /// or an empty list when the endpoint is not configured or the call fails.
    /// </summary>
    public async Task<IReadOnlyList<FoundryDeployment>> ListAsync(CancellationToken cancellationToken)
    {
        var endpoint = ResolveEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogDebug("FoundryDeploymentLister: no AiFoundry/AzureOpenAI endpoint configured.");
            return [];
        }

        var cacheKey = endpoint;
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.UtcNow - cached.At < CacheDuration)
        {
            return cached.Deployments;
        }

        var deployments = await FetchAsync(endpoint, cancellationToken);
        _cache[cacheKey] = new CacheEntry(deployments, DateTimeOffset.UtcNow);
        return deployments;
    }

    private async Task<IReadOnlyList<FoundryDeployment>> FetchAsync(string endpoint, CancellationToken ct)
    {
        var resourceId = TryBuildResourceId(endpoint);
        if (resourceId is null)
        {
            _logger.LogWarning(
                "FoundryDeploymentLister: cannot derive ARM resource id from endpoint {Endpoint}. " +
                "Configure AiFoundry:SubscriptionId / ResourceGroup / AccountName explicitly.",
                endpoint);
            return [];
        }

        var armUrl =
            $"https://management.azure.com{resourceId}/deployments" +
            $"?api-version={ApiVersion}";

        var client = _httpFactory.CreateClient("AiFoundry");
        client.Timeout = TimeSpan.FromSeconds(8);

        var req = new HttpRequestMessage(HttpMethod.Get, armUrl);
        AttachAuth(req);

        try
        {
            var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "FoundryDeploymentLister: ARM returned {Status} for {Url}. Body: {Body}",
                    (int)resp.StatusCode, armUrl, Truncate(body, 256));
                return [];
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            return Parse(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FoundryDeploymentLister: ARM GET failed for {Url}", armUrl);
            return [];
        }
    }

    private void AttachAuth(HttpRequestMessage req)
    {
        // Prefer explicit API key (works for both AiFoundry and AzureOpenAI keys),
        // fall back to AAD token via DefaultAzureCredential against management.azure.com.
        var key = _config["AiFoundry:Key"] ?? _config["AzureOpenAI:Key"];
        if (!string.IsNullOrWhiteSpace(key))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return;
        }

        try
        {
            var credential = new DefaultAzureCredential();
            var tokenRequest = new TokenRequestContext(new[] { "https://management.azure.com/.default" });
            var token = credential.GetToken(tokenRequest, default);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FoundryDeploymentLister: DefaultAzureCredential failed.");
        }
    }

    private string? ResolveEndpoint()
    {
        // AiFoundry and AzureOpenAI can target the same account. Prefer AiFoundry.
        return _config["AiFoundry:Endpoint"] ?? _config["AzureOpenAI:Endpoint"];
    }

    private string? TryBuildResourceId(string endpoint)
    {
        // Explicit configuration always wins — these three values can be set in
        // appsettings.json under AiFoundry.
        var sub = _config["AiFoundry:SubscriptionId"];
        var rg = _config["AiFoundry:ResourceGroup"];
        var acct = _config["AiFoundry:AccountName"];

        // Otherwise parse the hostname:
        //   https://po-aiservices-shared.cognitiveservices.azure.com/
        //       └─────────────────────────┘
        if (string.IsNullOrWhiteSpace(acct) && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            var host = uri.Host; // po-aiservices-shared.cognitiveservices.azure.com
            var first = host.Split('.')[0];
            if (!string.IsNullOrWhiteSpace(first) && first != "localhost")
            {
                acct = first;
            }
        }

        if (string.IsNullOrWhiteSpace(acct))
            return null;

        // Resource group is not in the endpoint URL; PoShared is the known naming
        // convention (see AGENT.MD). Operators can override via AiFoundry:ResourceGroup.
        if (string.IsNullOrWhiteSpace(rg))
        {
            rg = acct.StartsWith("po-", StringComparison.OrdinalIgnoreCase)
                ? "PoShared"
                : "DefaultResourceGroup";
        }

        // Subscription: fall back to a configured value; otherwise return a partial
        // resource id that ARM may still accept if the credential has access.
        if (string.IsNullOrWhiteSpace(sub))
        {
            _logger.LogInformation(
                "FoundryDeploymentLister: AiFoundry:SubscriptionId not configured. " +
                "Set AiFoundry:SubscriptionId / ResourceGroup / AccountName for full enumeration.");
            return null;
        }

        return $"/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{acct}";
    }

    private static IReadOnlyList<FoundryDeployment> Parse(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<DeploymentListResponse>(json, JsonOpts);
            if (doc?.Value is null) return [];

            return doc.Value
                .Where(d => !string.IsNullOrWhiteSpace(d.Name) && d.Properties?.Model is not null)
                .Select(d => new FoundryDeployment(
                    Name: d.Name!,
                    ModelName: d.Properties!.Model!.Name ?? d.Name!,
                    ModelVersion: d.Properties.Model.Version,
                    ProvisioningState: d.Properties.ProvisioningState,
                    Capacity: d.Sku?.Capacity,
                    SkuName: d.Sku?.Name))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private sealed record CacheEntry(IReadOnlyList<FoundryDeployment> Deployments, DateTimeOffset At);

    // ── Wire-format types ───────────────────────────────────────────────────
    private sealed class DeploymentListResponse
    {
        [JsonPropertyName("value")]
        public List<DeploymentDto>? Value { get; set; }
    }

    private sealed class DeploymentDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("properties")]
        public DeploymentProps? Properties { get; set; }

        [JsonPropertyName("sku")]
        public DeploymentSku? Sku { get; set; }
    }

    private sealed class DeploymentProps
    {
        [JsonPropertyName("model")]
        public DeploymentModel? Model { get; set; }

        [JsonPropertyName("provisioningState")]
        public string? ProvisioningState { get; set; }
    }

    private sealed class DeploymentModel
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    private sealed class DeploymentSku
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("capacity")]
        public int? Capacity { get; set; }
    }
}

/// <summary>
/// A dropdown-friendly view of one AI Foundry deployment.
/// </summary>
public sealed record FoundryDeployment(
    string Name,
    string ModelName,
    string? ModelVersion,
    string? ProvisioningState,
    int? Capacity,
    string? SkuName);