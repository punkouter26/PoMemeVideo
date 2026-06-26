namespace PoMemeVideo.Domain.Interfaces;

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
