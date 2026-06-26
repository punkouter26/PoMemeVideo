// GoF: Adapter Pattern — wraps Azure OpenAI SDK to IDirectorService domain interface
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using PoMemeVideo.Api.Entities;
using Microsoft.Extensions.Hosting;
using PoMemeVideo.Api.Interfaces;
using PoMemeVideo.Shared.Enums;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.AzureOpenAi;

public sealed class AzureOpenAiDirectorService : IDirectorService
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

    private readonly ChatClient _chatClient;
    private readonly ILogger<AzureOpenAiDirectorService> _logger;
    private readonly string _endpoint;
    private readonly string _deployment;

    private readonly int _directorTimeoutSeconds;
    private readonly bool _isDevelopment;
    public AzureOpenAiDirectorService(
        IConfiguration config,
        IHostEnvironment environment,
        ILogger<AzureOpenAiDirectorService> logger)
    {
        _logger = logger;

        _isDevelopment = environment.IsDevelopment();
        var endpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("AzureOpenAI:Endpoint not configured.");
        _endpoint = endpoint;
        _deployment = config["AzureOpenAI:DirectorDeployment"] ?? "gpt-5.4-nano";
        _directorTimeoutSeconds = config.GetValue<int?>("AzureOpenAI:DirectorTimeoutSeconds")
            ?? (_isDevelopment ? 45 : 0);

        var key = config["AzureOpenAI:Key"];
        AzureOpenAIClient client = string.IsNullOrWhiteSpace(key)
            ? new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));

        _chatClient = client.GetChatClient(_deployment);
        _logger.LogInformation(
            "AzureOpenAiDirectorService initialized. Endpoint={Endpoint}, Deployment={Deployment}, AuthMode={AuthMode}, IsDevelopment={IsDevelopment}, DirectorTimeoutSeconds={TimeoutSeconds}",
            _endpoint,
            _deployment,
            string.IsNullOrWhiteSpace(key) ? "DefaultAzureCredential" : "ApiKey",
            _isDevelopment,
            _directorTimeoutSeconds);
    }

    public async Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default)
    {
        var labelsJson = JsonSerializer.Serialize(
            visionLabels.Select(v => new { v.TimestampSeconds, v.Label }), JsonOpts);
        var soundsJson = JsonSerializer.Serialize(
            topCandidates.Select(s => new { s.SoundId, s.DisplayName, Tags = s.ActionVectorTags }), JsonOpts);

        _logger.LogInformation(
            "Session {SessionId}: Azure director start. VisionLabels={VisionLabelCount}, Candidates={CandidateCount}, Endpoint={Endpoint}, Deployment={Deployment}, HasRealVision={HasRealVision}",
            sessionId,
            visionLabels.Length,
            topCandidates.Count,
            _endpoint,
            _deployment,
            hasRealVisionData);

        var prompt = DirectorPrompt.Build(labelsJson, soundsJson, hasRealVisionData);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an expert meme video director. Respond only with valid JSON."),
            new UserChatMessage(prompt),
        };

        _logger.LogDebug(
            "Session {SessionId}: Azure director payload sizes. LabelsJsonChars={LabelsChars}, SoundsJsonChars={SoundsChars}, PromptChars={PromptChars}",
            sessionId,
            labelsJson.Length,
            soundsJson.Length,
            prompt.Length);

        using var timeoutCts = _directorTimeoutSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(_directorTimeoutSeconds))
            : null;
        using var linkedCts = timeoutCts is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : null;
        var effectiveToken = linkedCts?.Token ?? cancellationToken;

        var callStopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Session {SessionId}: Calling Azure OpenAI CompleteChatAsync... TimeoutSeconds={TimeoutSeconds}",
            sessionId,
            _directorTimeoutSeconds > 0 ? _directorTimeoutSeconds : -1);

        ChatCompletion response;
        try
        {
            response = await _chatClient.CompleteChatAsync(messages, cancellationToken: effectiveToken);
        }
        catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            callStopwatch.Stop();
            _logger.LogError(
                "Session {SessionId}: Azure OpenAI director call timed out after {ElapsedMs} ms (configured timeout {TimeoutSeconds}s).",
                sessionId,
                callStopwatch.ElapsedMilliseconds,
                _directorTimeoutSeconds);
            throw new TimeoutException($"Azure OpenAI director timed out after {_directorTimeoutSeconds}s for session {sessionId}.");
        }
        callStopwatch.Stop();
        _logger.LogInformation(
            "Session {SessionId}: Azure OpenAI CompleteChatAsync completed in {ElapsedMs} ms. Choices={ChoiceCount}",
            sessionId,
            callStopwatch.ElapsedMilliseconds,
            response.Content.Count);

        var rawText = response.Content[0].Text.Trim();
        _logger.LogDebug(
            "Session {SessionId}: Azure director raw response length={RawChars}. First200={Preview}",
            sessionId,
            rawText.Length,
            rawText.Length > 200 ? rawText[..200] : rawText);

        var json = rawText.Trim();
        if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
        if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];

        try
        {
            var parseStopwatch = Stopwatch.StartNew();
            var dtos = JsonSerializer.Deserialize<DirectorEntry[]>(json.Trim(), JsonOpts) ?? [];
            parseStopwatch.Stop();
            _logger.LogInformation(
                "Session {SessionId}: Azure director JSON parsed in {ElapsedMs} ms. EntryCount={EntryCount}",
                sessionId,
                parseStopwatch.ElapsedMilliseconds,
                dtos.Length);

            if (dtos.Length == 0)
            {
                _logger.LogWarning("Session {SessionId}: AzureOpenAI director returned an empty script. Raw response: {Raw}", sessionId, rawText);
                return [];
            }

            // Build a lookup: display name → SoundId for resolving cases where LLM echoes the name instead of the GUID
            // Use GroupBy+First to tolerate duplicate names in topCandidates (shouldn't happen, but LLM can hallucinate)
            var byName = topCandidates
                .GroupBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().SoundId, StringComparer.OrdinalIgnoreCase);
            var byId = topCandidates
                .GroupBy(s => s.SoundId.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().SoundId, StringComparer.OrdinalIgnoreCase);

            Guid ResolveSound(string raw)
            {
                if (byId.TryGetValue(raw, out var byIdMatch)) return byIdMatch;
                if (byName.TryGetValue(raw, out var byNameMatch)) return byNameMatch;
                if (Guid.TryParse(raw, out var parsed)) return parsed;
                // Final fallback: first candidate
                return topCandidates.Count > 0 ? topCandidates[0].SoundId : Guid.Empty;
            }

            var entries = dtos.Select(e => new ScriptEntry
            {
                EntryId = Guid.NewGuid(),
                SessionId = sessionId,
                TimestampMs = e.TimestampMs,
                SoundId = ResolveSound(e.SoundIdRaw),
                ActionVectorTags = e.ActionVectorTags,
                SceneDescription = e.SceneDescription,
                SelectionRationale = e.SelectionRationale,
                IsIronic = e.IsIronic,
                VisualEffect = e.VisualEffect,
                EffectIntensity = e.EffectIntensity,
                PlacementType = PlacementType.Triggered,
            }).ToArray();

            _logger.LogInformation(
                "Session {SessionId}: Azure director mapped {EntryCount} ScriptEntry object(s).",
                sessionId,
                entries.Length);

            return entries;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId}: AzureOpenAI director failed to parse response. Raw: {Raw}", sessionId, rawText);
            return [];
        }
    }

    private sealed class DirectorEntry
    {
        [JsonPropertyName("timestampMs")]
        public long TimestampMs { get; init; }

        [JsonPropertyName("soundId")]
        public string SoundIdRaw { get; init; } = string.Empty;

        [JsonPropertyName("actionVectorTags")]
        public string[] ActionVectorTags { get; init; } = [];

        [JsonPropertyName("sceneDescription")]
        public string SceneDescription { get; init; } = string.Empty;

        [JsonPropertyName("selectionRationale")]
        public string SelectionRationale { get; init; } = string.Empty;

        [JsonPropertyName("isIronic")]
        public bool IsIronic { get; init; }

        [JsonPropertyName("visualEffect")]
        public VisualEffectType? VisualEffect { get; init; }

        [JsonPropertyName("effectIntensity")]
        public double? EffectIntensity { get; init; }
    }
}
