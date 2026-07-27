namespace PoMemeVideo.Api.Features.Output;

public interface IVideoRenderService
{
    /// <summary>
    /// Queues and awaits the render job. Returns when FFmpeg has completed and output is uploaded.
    /// </summary>
    Task RenderAsync(RenderJob job, CancellationToken cancellationToken = default);
}

public record RenderJob(
    SessionId SessionId,
    string SourceBlobPath,
    string OutputBlobPath,
    bool AggressiveVisuals,
    IReadOnlyList<RenderSoundEntry> SoundEntries)
{
    /// <summary>Internal completion signal set by FFmpegRenderService when done.</summary>
    public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Set by FFmpegRenderService after render via ffprobe. 0 if unavailable.</summary>
    public double ActualDurationSeconds { get; set; }
};

public record RenderSoundEntry(
    long TimestampMs,
    string SoundBlobUrl,
    string? VisualEffect,
    double? EffectIntensity,
    string? OverlayAssetId);
