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
            "create a DirectorScript that maps meme sounds to moments in the video.\n\n" +
            $"Action labels (JSON): {labelsJson}\n" +
            $"Available sounds (JSON): {soundsJson}\n\n" +
            "Return ONLY a JSON array of script entries. Each entry MUST include:\n" +
            "- sceneDescription: a vivid 1-2 sentence description of what is happening in the scene at that moment\n" +
            "- selectionRationale: explain specifically why this meme sound fits the scene (tone, timing, irony, cultural reference)\n" +
            "- soundId: must match one of the provided soundId values exactly\n" +
            "- timestampMs: timestamp in milliseconds\n" +
            "- actionVectorTags: array of action tags\n" +
            "- isIronic: true if the sound choice is ironic/subversive\n" +
            "- visualEffect: one of None, DeepFry, SnapZoom, MotionBlur, Overlay\n" +
            "- effectIntensity: 0.0 to 1.0\n\n" +
            "Example format:\n" +
            "[{\"timestampMs\": 3000, \"soundId\": \"...\", \"actionVectorTags\": [\"explosion\"], " +
            "\"sceneDescription\": \"A character stumbles backward after being hit, arms flailing wildly.\", " +
            "\"selectionRationale\": \"The comedic impact sound perfectly punctuates the physical chaos and amplifies the absurdity.\", " +
            "\"isIronic\": false, \"visualEffect\": \"SnapZoom\", \"effectIntensity\": 0.8}]\n" +
            "No extra text outside the JSON array.";

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
