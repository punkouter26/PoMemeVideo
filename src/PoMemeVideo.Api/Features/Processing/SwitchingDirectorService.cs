namespace PoMemeVideo.Api.Features.Processing;

/// <summary>
/// Delegates to the correct AI backend based on the current RuntimeAiSettings.
/// GoF: Strategy Pattern — swappable AI backend at runtime without restart.
/// </summary>
public sealed class SwitchingDirectorService : IDirectorService
{
    private readonly RuntimeAiSettings _settings;
    private readonly AzureOpenAiDirectorService _azure;
    private readonly AiFoundryDirectorService _foundry;
    private readonly BrowserLLMDirectorService _browser;

    public SwitchingDirectorService(
        RuntimeAiSettings settings,
        AzureOpenAiDirectorService azure,
        AiFoundryDirectorService foundry,
        BrowserLLMDirectorService browser)
    {
        _settings = settings;
        _azure = azure;
        _foundry = foundry;
        _browser = browser;
    }

    /// <summary>Which backend a given provider string selects.</summary>
    internal enum Backend
    {
        AzureOpenAi,
        AiFoundry,
        BrowserLlm,
    }

    /// <summary>
    /// The dispatch rule, split out from <see cref="DirectAsync"/> so it can be tested without
    /// constructing the directors — the cloud ones are sealed and build a live Azure client in
    /// their constructor. AiFoundry is the fallback rather than a throw: <c>Provider</c> is
    /// runtime-mutable via <c>PUT /api/config/ai-model</c>, and an unrecognised value (a
    /// settings file written by an older build, say) must still render a video.
    /// </summary>
    internal static Backend SelectBackend(string? provider) => provider switch
    {
        not null when provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase) => Backend.AzureOpenAi,
        not null when provider.Equals("BrowserLLM", StringComparison.OrdinalIgnoreCase) => Backend.BrowserLlm,
        _ => Backend.AiFoundry,
    };

    public Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        SessionId sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default)
    {
        IDirectorService target = SelectBackend(_settings.Provider) switch
        {
            Backend.AzureOpenAi => _azure,
            Backend.BrowserLlm => _browser,
            _ => _foundry,
        };

        return target.DirectAsync(visionLabels, topCandidates, sessionId, hasRealVisionData, cancellationToken);
    }
}
