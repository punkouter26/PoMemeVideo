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

    public async Task<(double TimestampSeconds, string Label)[]> AnalyseAsync(
        string[] keyframeBase64Images,
        CancellationToken cancellationToken = default)
    {
        var contentParts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(
                "Analyse these video keyframes taken at 3-second intervals. " +
                "Identify semantic action labels for meme-worthy moments. " +
                "Return ONLY a JSON array like: [{\"timestamp_seconds\": 3.0, \"label\": \"explosion\"}]. " +
                "No other text."),
        };

        for (var i = 0; i < keyframeBase64Images.Length; i++)
        {
            contentParts.Add(ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(Convert.FromBase64String(keyframeBase64Images[i])),
                "image/png"));
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a meme director AI. Analyse video frames and identify semantic triggers."),
            new UserChatMessage(contentParts),
        };

        var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        var text = response.Value.Content[0].Text.Trim();
        _logger.LogInformation("GPT-4o Vision raw response: {Response}", text);

        // Strip markdown code fences if GPT-4o wrapped the JSON (e.g. ```json ... ```)
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
            _logger.LogInformation("GPT-4o Vision parsed {Count} label(s): {Labels}",
                items.Length, string.Join(", ", items.Select(x => $"t={x.TimestampSeconds:F1}s→{x.Label}")));
            return items.Select(x => (x.TimestampSeconds, x.Label)).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPT-4o Vision JSON parse failed. Raw text was: {Text}", text);
            return [(0.0, "unknown action")];
        }
    }

    private sealed record VisionLabel(
        [property: JsonPropertyName("timestamp_seconds")] double TimestampSeconds,
        [property: JsonPropertyName("label")] string Label);
}
