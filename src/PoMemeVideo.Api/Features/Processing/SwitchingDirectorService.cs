
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
    private readonly OllamaDirectorService _ollama;
    private readonly BrowserLLMDirectorService _browser;

    public SwitchingDirectorService(
        RuntimeAiSettings settings,
        AzureOpenAiDirectorService azure,
        AiFoundryDirectorService foundry,
        OllamaDirectorService ollama,
        BrowserLLMDirectorService browser)
    {
        _settings = settings;
        _azure = azure;
        _foundry = foundry;
        _ollama = ollama;
        _browser = browser;
    }

    public Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default)
    {
        return _settings.Provider switch
        {
            "AzureOpenAI" => _azure.DirectAsync(visionLabels, topCandidates, sessionId, hasRealVisionData, cancellationToken),
            "AiFoundry" => _foundry.DirectAsync(visionLabels, topCandidates, sessionId, hasRealVisionData, cancellationToken),
            "Ollama" => _ollama.DirectAsync(visionLabels, topCandidates, sessionId, hasRealVisionData, cancellationToken),
            _ => _browser.DirectAsync(visionLabels, topCandidates, sessionId, hasRealVisionData, cancellationToken),
        };
    }
}
