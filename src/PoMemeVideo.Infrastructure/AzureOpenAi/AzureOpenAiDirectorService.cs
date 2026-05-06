// GoF: Adapter Pattern — wraps Azure OpenAI SDK to IDirectorService domain interface
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Infrastructure.AzureOpenAi;

public sealed class AzureOpenAiDirectorService : IDirectorService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ChatClient _chatClient;

    public AzureOpenAiDirectorService(IConfiguration config)
    {
        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint not configured.");

        var key = config["AzureOpenAI:Key"];
        AzureOpenAIClient client = string.IsNullOrWhiteSpace(key)
            ? new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));

        _chatClient = client.GetChatClient("gpt-4o");
    }

    public async Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var labelsJson = JsonSerializer.Serialize(
            visionLabels.Select(v => new { v.TimestampSeconds, v.Label }), JsonOpts);
        var soundsJson = JsonSerializer.Serialize(
            topCandidates.Select(s => new { s.SoundId, s.DisplayName, Tags = s.ActionVectorTags }), JsonOpts);

        var prompt =
            "You are a meme video director. Given video action labels and available meme sounds, " +
            "create a DirectorScript mapping sounds to moments.\n\n" +
            $"Action labels (JSON): {labelsJson}\n" +
            $"Available sounds (JSON): {soundsJson}\n\n" +
            "Return ONLY a JSON array of script entries like:\n" +
            "[{\"timestampMs\": 3000, \"soundId\": \"...\", \"actionVectorTags\": [\"explosion\"], " +
            "\"selectionRationale\": \"why this sound fits\", \"isIronic\": false, " +
            "\"visualEffect\": \"DeepFry\", \"effectIntensity\": 0.8}]\n" +
            "No extra text. VisualEffect must be one of: None, DeepFry, SnapZoom, MotionBlur, Overlay.";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an expert meme video director. Respond only with valid JSON."),
            new UserChatMessage(prompt),
        };

        var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        var rawText = response.Value.Content[0].Text.Trim();

        var json = rawText.Trim();
        if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
        if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];

        try
        {
            return JsonSerializer.Deserialize<ScriptEntry[]>(json.Trim(), JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
