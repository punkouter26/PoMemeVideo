// GoF: Adapter Pattern — wraps Azure OpenAI SDK to IAiVisionService domain interface
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using PoMemeVideo.Domain.Interfaces;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Infrastructure.AzureOpenAi;

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

    public AzureOpenAiVisionService(IConfiguration config, ILogger<AzureOpenAiVisionService> logger)
    {
        _logger = logger;
        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint not configured.");

        var key = config["AzureOpenAI:Key"];
        AzureOpenAIClient client = string.IsNullOrWhiteSpace(key)
            ? new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));

        _chatClient = client.GetChatClient("gpt-4o");
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

        for (var batchStart = 0; batchStart < keyframeBase64Images.Length; batchStart += VisionBatchSize)
        {
            var batch = keyframeBase64Images.Skip(batchStart).Take(VisionBatchSize).ToArray();
            var batchOffsetSeconds = batchStart * FrameIntervalSeconds;
            var batchResults = await AnalyseBatchAsync(batch, batchOffsetSeconds, cancellationToken);
            allResults.AddRange(batchResults);
        }

        _logger.LogInformation("GPT-4o Vision total: {Count} label(s) from {Batches} batch(es): {Labels}",
            allResults.Count,
            (int)Math.Ceiling(keyframeBase64Images.Length / (double)VisionBatchSize),
            string.Join(", ", allResults.Select(x => $"t={x.TimestampSeconds:F1}s→{x.Label}")));

        return [.. allResults];
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
                "Identify semantic action labels ONLY for genuinely meme-worthy moments (do not force labels on boring frames). " +
                "Return ONLY a JSON array like: [{\"timestamp_seconds\": " + (startOffsetSeconds + FrameIntervalSeconds).ToString("F1") + ", \"label\": \"explosion\"}]. " +
                "Return [] if there are no interesting moments. No other text."),
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

        var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        var text = response.Value.Content[0].Text.Trim();
        _logger.LogInformation("GPT-4o Vision raw response (offset={Offset}s): {Response}", startOffsetSeconds, text);

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
            _logger.LogWarning(ex, "GPT-4o Vision JSON parse failed for batch at offset {Offset}s. Raw: {Text}", startOffsetSeconds, text);
            return [];
        }
    }

    private sealed record VisionLabel(
        [property: JsonPropertyName("timestamp_seconds")] double TimestampSeconds,
        [property: JsonPropertyName("label")] string Label);
}
