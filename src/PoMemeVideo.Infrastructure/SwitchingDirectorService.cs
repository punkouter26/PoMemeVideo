using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure.AzureOpenAi;
using PoMemeVideo.Infrastructure.BrowserLlm;
using PoMemeVideo.Infrastructure.Ollama;

namespace PoMemeVideo.Infrastructure;

/// <summary>
/// Delegates to AzureOpenAiDirectorService, OllamaDirectorService, or
/// BrowserLLMDirectorService based on the current RuntimeAiSettings.
/// GoF: Strategy Pattern — swappable AI backend at runtime without restart.
/// </summary>
public sealed class SwitchingDirectorService : IDirectorService
{
    private readonly RuntimeAiSettings _settings;
    private readonly AzureOpenAiDirectorService _azure;
    private readonly OllamaDirectorService _ollama;
    private readonly BrowserLLMDirectorService _browser;

    public SwitchingDirectorService(
        RuntimeAiSettings settings,
        AzureOpenAiDirectorService azure,
        OllamaDirectorService ollama,
        BrowserLLMDirectorService browser)
    {
        _settings = settings;
        _azure = azure;
        _ollama = ollama;
        _browser = browser;
    }

    public Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return _settings.Provider switch
        {
            "AzureOpenAI" => _azure.DirectAsync(visionLabels, topCandidates, sessionId, cancellationToken),
            "BrowserLLM"  => _browser.DirectAsync(visionLabels, topCandidates, sessionId, cancellationToken),
            _             => _ollama.DirectAsync(visionLabels, topCandidates, sessionId, cancellationToken),
        };
    }
}
