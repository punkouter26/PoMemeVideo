// GoF: Adapter — wraps Azure AI Foundry (AIServices) endpoint via Azure.AI.OpenAI SDK
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.Features.Processing;

public sealed class AiFoundryDirectorService : IDirectorService
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

    private readonly AzureOpenAIClient _client;
    private readonly ILogger<AiFoundryDirectorService> _logger;
    private readonly RuntimeAiSettings _settings;
    private readonly string _endpoint;
    private readonly int _timeoutSeconds;

    public AiFoundryDirectorService(
        IConfiguration config,
        IHostEnvironment environment,
        RuntimeAiSettings settings,
        ILogger<AiFoundryDirectorService> logger)
    {
        _logger = logger;
        _settings = settings;

        var endpoint = config["AiFoundry:Endpoint"]
            ?? throw new InvalidOperationException("AiFoundry:Endpoint not configured.");
        _endpoint = endpoint;

        _timeoutSeconds = config.GetValue<int?>("AiFoundry:TimeoutSeconds")
            ?? (environment.IsDevelopment() ? 60 : 0);

        var key = config["AiFoundry:Key"];
        _client = string.IsNullOrWhiteSpace(key)
            ? new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));

        _logger.LogInformation(
            "AiFoundryDirectorService initialized. Endpoint={Endpoint}, AuthMode={AuthMode}",
            _endpoint,
            string.IsNullOrWhiteSpace(key) ? "DefaultAzureCredential" : "ApiKey");
    }

    // Resolved per-call so runtime changes to AiFoundryDeployment are honoured.
    private ChatClient GetChatClient() => _client.GetChatClient(_settings.AiFoundryDeployment);

    public async Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default)
    {
        var deployment = _settings.AiFoundryDeployment;
        var labelsJson = JsonSerializer.Serialize(
            visionLabels.Select(v => new { v.TimestampSeconds, v.Label }), JsonOpts);
        var soundsJson = DirectorPrompt.SerializeSounds(topCandidates, JsonOpts);

        _logger.LogInformation(
            "Session {SessionId}: AI Foundry director start. Deployment={Deployment}, VisionLabels={VisionLabelCount}, Candidates={CandidateCount}, HasRealVision={HasRealVision}",
            sessionId, deployment, visionLabels.Length, topCandidates.Count, hasRealVisionData);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an expert meme video director. Respond only with valid JSON."),
            new UserChatMessage(DirectorPrompt.Build(labelsJson, soundsJson, hasRealVisionData)),
        };

        using var timeoutCts = _timeoutSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds))
            : null;
        using var linkedCts = timeoutCts is not null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token)
            : null;
        var effectiveToken = linkedCts?.Token ?? cancellationToken;

        var sw = Stopwatch.StartNew();
        ChatCompletion response;
        try
        {
            response = await GetChatClient().CompleteChatAsync(messages, cancellationToken: effectiveToken);
        }
        catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogError("Session {SessionId}: AI Foundry director timed out after {ElapsedMs} ms.", sessionId, sw.ElapsedMilliseconds);
            throw new TimeoutException($"AI Foundry director timed out after {_timeoutSeconds}s for session {sessionId}.");
        }
        sw.Stop();

        _logger.LogInformation(
            "Session {SessionId}: AI Foundry director completed in {ElapsedMs} ms. Deployment={Deployment}",
            sessionId, sw.ElapsedMilliseconds, deployment);

        var rawText = response.Content[0].Text.Trim();
        try
        {
            return DirectorPrompt.ParseResponse(rawText, sessionId, JsonOpts, topCandidates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {SessionId}: AI Foundry director failed to parse response. Raw: {Raw}", sessionId, rawText);
            return [];
        }
    }
}
