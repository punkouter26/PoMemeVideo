namespace PoMemeVideo.Domain.Interfaces;

public interface IVideoRenderService
{
    Task RenderAsync(RenderJob job, CancellationToken cancellationToken = default);
}

public record RenderJob(
    Guid SessionId,
    string SourceBlobPath,
    string OutputBlobPath,
    bool AggressiveVisuals,
    IReadOnlyList<RenderSoundEntry> SoundEntries);

public record RenderSoundEntry(
    long TimestampMs,
    string SoundBlobUrl,
    string? VisualEffect,
    double? EffectIntensity,
    string? OverlayAssetId);
