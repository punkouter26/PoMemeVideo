using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure.AzureOpenAi;
using PoMemeVideo.Infrastructure.Ollama;

namespace PoMemeVideo.Infrastructure;

/// <summary>
/// Delegates to either AzureOpenAiDirectorService or OllamaDirectorService
/// based on the current RuntimeAiSettings. Registered as the IDirectorService.
/// </summary>
public sealed class SwitchingDirectorService : IDirectorService
{
    private readonly RuntimeAiSettings _settings;
    private readonly AzureOpenAiDirectorService _azure;
    private readonly OllamaDirectorService _ollama;

    public SwitchingDirectorService(
        RuntimeAiSettings settings,
        AzureOpenAiDirectorService azure,
        OllamaDirectorService ollama)
    {
        _settings = settings;
        _azure = azure;
        _ollama = ollama;
    }

    public Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return _settings.Provider == "AzureOpenAI"
            ? _azure.DirectAsync(visionLabels, topCandidates, sessionId, cancellationToken)
            : _ollama.DirectAsync(visionLabels, topCandidates, sessionId, cancellationToken);
    }
}
