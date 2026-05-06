// GoF: Adapter Pattern — wraps Ollama HTTP API to IDirectorService domain interface
using Microsoft.Extensions.Configuration;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Infrastructure.Ollama;

public sealed class OllamaDirectorService : IDirectorService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly RuntimeAiSettings _aiSettings;
    private readonly string _ollamaBaseUrl;

    public OllamaDirectorService(IHttpClientFactory httpFactory, RuntimeAiSettings aiSettings, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _aiSettings = aiSettings;
        _ollamaBaseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
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

        var prompt = "You are a meme video director. Given video action labels and available meme sounds, " +
            "create a DirectorScript mapping sounds to moments.\n\n" +
            $"Action labels (JSON): {labelsJson}\n" +
            $"Available sounds (JSON): {soundsJson}\n\n" +
            "Return ONLY a JSON array of script entries like:\n" +
            "[{\"timestampMs\": 3000, \"soundId\": \"...\", \"actionVectorTags\": [\"explosion\"], " +
            "\"selectionRationale\": \"why this sound fits\", \"isIronic\": false, " +
            "\"visualEffect\": \"DeepFry\", \"effectIntensity\": 0.8}]\n" +
            "No extra text. VisualEffect must be one of: None, DeepFry, SnapZoom, MotionBlur, Overlay.";

        var requestBody = new { model = _aiSettings.OllamaModel, prompt, stream = false };
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(_ollamaBaseUrl);
        var response = await http.PostAsJsonAsync("/api/generate", requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(JsonOpts, cancellationToken);
        var rawText = result?.Response ?? "[]";

        // Strip markdown code fences if present
        var json = rawText.Trim();
        if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
        if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];

        try
        {
            var directorEntries = JsonSerializer.Deserialize<DirectorEntry[]>(json, JsonOpts) ?? [];
            return directorEntries.Select(e => new ScriptEntry
            {
                EntryId = Guid.NewGuid(),
                SessionId = sessionId,
                TimestampMs = e.TimestampMs,
                SoundId = e.SoundId,
                ActionVectorTags = e.ActionVectorTags,
                SelectionRationale = e.SelectionRationale,
                IsIronic = e.IsIronic,
                VisualEffect = e.VisualEffect,
                EffectIntensity = e.EffectIntensity,
                PlacementType = PlacementType.Triggered,
            }).ToArray();
        }
        catch
        {
            // Fallback: basic entries from vision labels
            return visionLabels.Select((v, i) => new ScriptEntry
            {
                EntryId = Guid.NewGuid(),
                SessionId = sessionId,
                TimestampMs = (long)(v.TimestampSeconds * 1000),
                SoundId = topCandidates.Count > i ? topCandidates[i].SoundId : topCandidates[0].SoundId,
                ActionVectorTags = [v.Label],
                SelectionRationale = $"[FALLBACK] Ollama parse failed. Direct match for '{v.Label}'.",
                PlacementType = PlacementType.Triggered,
            }).ToArray();
        }
    }

    private sealed record OllamaResponse([property: JsonPropertyName("response")] string Response);

    private sealed class DirectorEntry
    {
        public long TimestampMs { get; init; }
        public Guid SoundId { get; init; }
        public string[] ActionVectorTags { get; init; } = [];
        public string SelectionRationale { get; init; } = string.Empty;
        public bool IsIronic { get; init; }
        public VisualEffectType? VisualEffect { get; init; }
        public double? EffectIntensity { get; init; }
    }
}
