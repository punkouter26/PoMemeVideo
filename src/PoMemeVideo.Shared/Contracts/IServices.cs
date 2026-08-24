// Cross-slice service contracts. Implementations live inside their owning slice; consumers
// resolve the interface from DI so no slice takes a compile-time dependency on a sibling.
using PoMemeVideo.Shared.Domain;
using PoMemeVideo.Shared.Models;

namespace PoMemeVideo.Shared.Contracts;

public interface IBlobStorageService
{
    Task<Stream> StreamBlobAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken = default);
    Task UploadBlobAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task UploadFileAsync(string path, string localFilePath, string contentType, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> ListBlobsByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    Task DeleteBlobsByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}

public interface IAiVisionService
{
    /// <summary>
    /// Analyses keyframe images and returns a list of timestamped semantic action labels.
    /// </summary>
    /// <param name="keyframeBase64Images">Base64-encoded PNG images at 3-second intervals.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of (TimestampSeconds, Label) tuples identifying semantic trigger events.</returns>
    Task<(double TimestampSeconds, string Label)[]> AnalyseAsync(
        string[] keyframeBase64Images,
        CancellationToken cancellationToken = default);
}

public interface IDirectorService
{
    /// <summary>
    /// Given AI Vision labels and top sound candidates, produces a full DirectorScript with
    /// placement rationale and ironic pairing flags.
    /// </summary>
    Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        SessionId sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default);
}

/// <summary>A sound scored against the current scene, highest score first.</summary>
public sealed record SoundCandidate(SoundAsset Sound, float Score);

/// <summary>Ranks the sound library against a vision label. Implemented by the MemeLibrary slice.</summary>
public interface ISemanticMatchingService
{
    Task<IReadOnlyList<SoundCandidate>> GetTopCandidatesAsync(
        string actionLabel,
        int topN = 3,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Enumerates models installed on the local Ollama host. Implemented by the Processing slice so
/// the Config slice can populate the model picker without depending on it.
/// </summary>
public interface ILocalModelCatalog
{
    Task<string[]?> GetInstalledModelsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Muxes a finished director script onto the source video. Implemented by the Output slice so
/// Processing can trigger a render without depending on it.
/// </summary>
public interface IRenderVideoCommand
{
    Task ExecuteAsync(
        SessionId sessionId,
        UserId userId,
        VideoSession session,
        DirectorScript script,
        CancellationToken cancellationToken = default);
}

// SOLID: Dependency Inversion — application code depends on IEngineNotifier,
// never on IHubContext<EngineHub>.
public interface IEngineNotifier
{
    Task DirectorLogAsync(SessionId sessionId, string message, CancellationToken cancellationToken = default);
    Task DirectorScriptAsync(SessionId sessionId, ScriptEntryDto entry, CancellationToken cancellationToken = default);
    Task AuditAsync(SessionId sessionId, string message, CancellationToken cancellationToken = default);
    Task HardwareMetricsAsync(SessionId sessionId, double inferenceLatencyMs, double cpuLoadPercent, CancellationToken cancellationToken = default);
    Task CompleteAsync(SessionId sessionId, string outputBlobPath, CancellationToken cancellationToken = default);
    Task ErrorAsync(SessionId sessionId, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the connected browser client to run local inference via Transformers.js
    /// and POST the result back to /api/processing/sessions/{id}/browser-director-result.
    /// </summary>
    Task BrowserLLMInferenceRequestAsync(SessionId sessionId, string payloadJson, CancellationToken cancellationToken = default);
}
