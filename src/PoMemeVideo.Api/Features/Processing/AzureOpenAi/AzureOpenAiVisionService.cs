// GoF: Adapter Pattern — wraps Azure OpenAI SDK to IAiVisionService domain interface
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.Features.Processing;

public sealed class AzureOpenAiVisionService : IAiVisionService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowDuplicateProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiVisionService> _logger;

    public AzureOpenAiVisionService(
        IConfiguration config,
        IHostEnvironment environment,
        ILogger<AzureOpenAiVisionService> logger)
    {
        _logger = logger;
        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint not configured.");

        var key = config["AzureOpenAI:Key"];

        // Non-null in test environments: routes the SDK through AiInterceptionHandler so no
        // tokens are spent while the real client/serialisation path is still exercised.
        var options = AiInterception.BuildClientOptions(environment, config);

        AzureOpenAIClient client = string.IsNullOrWhiteSpace(key)
            ? new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential(), options)
            : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key), options);

        // Vision model. Configurable via AzureOpenAI:VisionDeployment so operators can
        // route around quota contention on the default 1-capacity gpt-5.4-nano.
        var deployment = config["AzureOpenAI:VisionDeployment"] ?? "gpt-5.4-mini";
        _chatClient = client.GetChatClient(deployment);
    }

    private const int VisionBatchSize = 8;
    private const double FrameIntervalSeconds = 3.0;

    public async Task<(double TimestampSeconds, string Label)[]> AnalyseAsync(
        string[] keyframeBase64Images,
        CancellationToken cancellationToken = default)
    {
        if (keyframeBase64Images.Length == 0)
            return [];

        var allResults = new List<(double TimestampSeconds, string Label)>();
        var tasks = new List<Task<(double TimestampSeconds, string Label)[]>>();
        using var semaphore = new SemaphoreSlim(3);

        for (var batchStart = 0; batchStart < keyframeBase64Images.Length; batchStart += VisionBatchSize)
        {
            var batch = keyframeBase64Images.Skip(batchStart).Take(VisionBatchSize).ToArray();
            var batchOffsetSeconds = batchStart * FrameIntervalSeconds;

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await AnalyseBatchAsync(batch, batchOffsetSeconds, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        var batchOutputs = await Task.WhenAll(tasks);
        foreach (var batchResult in batchOutputs)
        {
            allResults.AddRange(batchResult);
        }

        _logger.LogInformation("GPT-5.4 Nano Vision total: {Count} label(s) from {Batches} batch(es): {Labels}",
            allResults.Count,
            (int)Math.Ceiling(keyframeBase64Images.Length / (double)VisionBatchSize),
            string.Join(", ", allResults.Select(x => $"t={x.TimestampSeconds:F1}s→{x.Label}")));

        return [.. allResults.OrderBy(x => x.TimestampSeconds)];
    }

    private async Task<(double TimestampSeconds, string Label)[]> AnalyseBatchAsync(
        string[] batchImages,
        double startOffsetSeconds,
        CancellationToken cancellationToken)
    {
        var contentParts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(
                $"Analyse these {batchImages.Length} video keyframe(s) taken at {FrameIntervalSeconds}-second intervals " +
                $"starting at t={startOffsetSeconds:F1}s. " +
                "For EVERY frame, you MUST return at least one label describing what is visible — at minimum the broad scene type. " +
                "Even 'static room', 'person standing', or 'text on screen' counts. " +
                "Identify meme-worthy moments if present, but NEVER return an empty array. " +
                "Return ONLY a JSON array like: [{\"timestamp_seconds\": " + (startOffsetSeconds + FrameIntervalSeconds).ToString("F1") + ", \"label\": \"explosion\"}]. " +
                "If truly nothing is happening, return [{\"timestamp_seconds\": " + (startOffsetSeconds + FrameIntervalSeconds).ToString("F1") + ", \"label\": \"static scene\"}]. " +
                "No other text."),
        };

        for (var i = 0; i < batchImages.Length; i++)
        {
            contentParts.Add(ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(Convert.FromBase64String(batchImages[i])),
                "image/png"));
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a meme director AI. Analyse video frames and identify semantic triggers."),
            new UserChatMessage(contentParts),
        };

        var response = await CompleteWithRetryAsync(messages, startOffsetSeconds, cancellationToken);
        var text = response.Value.Content[0].Text.Trim();
        _logger.LogInformation("GPT-5.4 Nano Vision raw response (offset={Offset}s): {Response}", startOffsetSeconds, text);

        var json = text;
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline >= 0) json = json[(firstNewline + 1)..];
            var lastFence = json.LastIndexOf("```");
            if (lastFence >= 0) json = json[..lastFence].Trim();
        }

        try
        {
            var items = JsonSerializer.Deserialize<VisionLabel[]>(json, JsonOpts) ?? [];
            return items.Select(x => (x.TimestampSeconds, x.Label)).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPT-5.4 Nano Vision JSON parse failed for batch at offset {Offset}s. Raw: {Text}", startOffsetSeconds, text);
            return [];
        }
    }

    private const int MaxRetries = 2;
    private const int MaxBackoffSeconds = 4;

    /// <summary>
    /// Calls the chat completion endpoint with short exponential backoff on HTTP 429
    /// (too_many_requests). The vision endpoint shares quota with the director on a
    /// pooled Azure deployment, so transient throttling is expected under load; a
    /// brief retry recovers from momentary spikes without falling back to label-free
    /// time-based placement. Because vision runs on the synchronous ingestion path,
    /// the total backoff is intentionally capped (<see cref="MaxBackoffSeconds"/>):
    /// when the deployment is *sustained*-throttled the retries can't succeed anyway,
    /// so we fail fast to the fallback rather than block the user for the full
    /// server-advertised <c>Retry-After</c> window (often 30 s+).
    /// </summary>
    private async Task<System.ClientModel.ClientResult<ChatCompletion>> CompleteWithRetryAsync(
        List<ChatMessage> messages,
        double startOffsetSeconds,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            }
            catch (System.ClientModel.ClientResultException ex) when (ex.Status == 429 && attempt < MaxRetries)
            {
                var delay = ResolveRetryDelay(ex, attempt);
                _logger.LogWarning(
                    "GPT-5.4 Nano Vision throttled (HTTP 429) at offset {Offset}s. Retry {Attempt}/{Max} in {Delay}s.",
                    startOffsetSeconds, attempt + 1, MaxRetries, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static TimeSpan ResolveRetryDelay(System.ClientModel.ClientResultException ex, int attempt)
    {
        // Exponential backoff: 1s, 2s. Prefer the server-advertised Retry-After when
        // it is shorter, but never wait longer than the cap — vision is on the
        // synchronous ingestion path, so a long block hurts UX more than the lost labels.
        var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));

        var raw = ex.GetRawResponse();
        if (raw is not null
            && raw.Headers.TryGetValue("Retry-After", out var retryAfter)
            && int.TryParse(retryAfter, out var seconds)
            && seconds > 0
            && seconds < backoff.TotalSeconds)
        {
            backoff = TimeSpan.FromSeconds(seconds);
        }

        return backoff > TimeSpan.FromSeconds(MaxBackoffSeconds)
            ? TimeSpan.FromSeconds(MaxBackoffSeconds)
            : backoff;
    }

    private sealed record VisionLabel(
        [property: JsonPropertyName("timestamp_seconds")] double TimestampSeconds,
        [property: JsonPropertyName("label")] string Label);
}
