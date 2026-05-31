// Calls a local Ollama instance via its OpenAI-compatible HTTP API.
// Only available in Development — the dev check is enforced at the config endpoint level.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using System.Diagnostics;
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
        AllowDuplicateProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly HttpClient _http;
    private readonly ILogger<OllamaDirectorService> _logger;
    private readonly RuntimeAiSettings _settings;
    private readonly string _baseUrl;

    public OllamaDirectorService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        RuntimeAiSettings settings,
        ILogger<OllamaDirectorService> logger)
    {
        _logger = logger;
        _settings = settings;
        _baseUrl = (config["Ollama:BaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
        _http = httpClientFactory.CreateClient("Ollama");
        _http.BaseAddress = new Uri(_baseUrl + "/");
        _http.Timeout = TimeSpan.FromSeconds(config.GetValue<int?>("Ollama:TimeoutSeconds") ?? 120);

        _logger.LogInformation("OllamaDirectorService initialized. BaseUrl={BaseUrl}", _baseUrl);
    }

    public async Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var model = _settings.OllamaModel;
        var labelsJson = JsonSerializer.Serialize(
            visionLabels.Select(v => new { v.TimestampSeconds, v.Label }), JsonOpts);
        var soundsJson = JsonSerializer.Serialize(
            topCandidates.Select(s => new { s.SoundId, s.DisplayName, Tags = s.ActionVectorTags }), JsonOpts);

        _logger.LogInformation(
            "Session {SessionId}: Ollama director start. Model={Model}, VisionLabels={VisionLabelCount}, Candidates={CandidateCount}",
            sessionId, model, visionLabels.Length, topCandidates.Count);

        var requestBody = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = "You are an expert meme video director. Respond only with valid JSON." },
                new { role = "user",   content = DirectorPrompt.Build(labelsJson, soundsJson) },
            },
            stream = false,
        };

        var sw = Stopwatch.StartNew();
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _http.PostAsJsonAsync("v1/chat/completions", requestBody, JsonOpts, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Session {SessionId}: Ollama request failed. Is Ollama running at {BaseUrl}?", sessionId, _baseUrl);
            throw new InvalidOperationException($"Ollama is not reachable at {_baseUrl}. Start Ollama and try again.", ex);
        }
        sw.Stop();

        httpResponse.EnsureSuccessStatusCode();

        _logger.LogInformation("Session {SessionId}: Ollama director completed in {ElapsedMs} ms. Model={Model}", sessionId, sw.ElapsedMilliseconds, model);

        var completion = await httpResponse.Content.ReadFromJsonAsync<OllamaCompletionResponse>(JsonOpts, cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty response.");

        var rawText = completion.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
        try
        {
            return DirectorPrompt.ParseResponse(rawText, sessionId, JsonOpts, topCandidates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId}: Ollama director failed to parse response. Raw: {Raw}", sessionId, rawText);
            return [];
        }
    }

    /// <summary>
    /// Probes the local Ollama instance and returns the list of installed model names,
    /// or null if Ollama is not running.
    /// </summary>
    public async Task<string[]?> GetInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var probe = new HttpClient { BaseAddress = new Uri(_baseUrl + "/"), Timeout = TimeSpan.FromSeconds(3) };
            var tags = await probe.GetFromJsonAsync<OllamaTagsResponse>("api/tags", JsonOpts, cancellationToken);
            return tags?.Models?.Select(m => m.Name).OrderBy(n => n).ToArray() ?? [];
        }
        catch
        {
            return null; // Ollama not running
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────
    private sealed class OllamaCompletionResponse
    {
        [JsonPropertyName("choices")] public OllamaChoice[]? Choices { get; init; }
    }
    private sealed class OllamaChoice
    {
        [JsonPropertyName("message")] public OllamaMessage? Message { get; init; }
    }
    private sealed class OllamaMessage
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }
    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")] public OllamaModelEntry[]? Models { get; init; }
    }
    private sealed class OllamaModelEntry
    {
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    }
}
