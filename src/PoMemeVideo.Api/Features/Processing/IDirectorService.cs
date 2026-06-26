using PoMemeVideo.Api.Entities;

namespace PoMemeVideo.Api.Interfaces;

public interface IDirectorService
{
    /// <summary>
    /// Given AI Vision labels and top sound candidates, produces a full DirectorScript with
    /// placement rationale and ironic pairing flags.
    /// </summary>
    Task<ScriptEntry[]> DirectAsync(
        (double TimestampSeconds, string Label)[] visionLabels,
        IReadOnlyList<SoundAsset> topCandidates,
        Guid sessionId,
        bool hasRealVisionData = false,
        CancellationToken cancellationToken = default);
}
