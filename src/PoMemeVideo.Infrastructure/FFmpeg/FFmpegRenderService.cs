using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure.AzureStorage;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PoMemeVideo.Infrastructure.FFmpeg;

/// <summary>
/// FFmpeg-based video rendering service implementing IVideoRenderService.
/// Uses System.Threading.Channels for bounded job concurrency.
/// GoF: Template Method — filter_complex construction varies per effect type.
/// </summary>
public class FFmpegRenderService : IVideoRenderService, IAsyncDisposable
{
    private readonly BlobStorageService _blobService;
    private readonly ILogger<FFmpegRenderService> _logger;
    private readonly Channel<RenderJob> _jobQueue;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;

    public FFmpegRenderService(BlobStorageService blobService, ILogger<FFmpegRenderService> logger)
    {
        _blobService = blobService;
        _logger = logger;
        _jobQueue = Channel.CreateBounded<RenderJob>(
            new BoundedChannelOptions(Environment.ProcessorCount) 
            { 
                FullMode = BoundedChannelFullMode.Wait 
            });
    }

    /// <summary>
    /// Queues a render job for processing.
    /// </summary>
    public async Task RenderAsync(
        RenderJob job,
        CancellationToken cancellationToken = default)
    {
        await _jobQueue.Writer.WriteAsync(job, cancellationToken);

        _logger.LogInformation(
            "FFmpeg render job queued for session {SessionId}: output → {OutputPath}",
            job.SessionId, job.OutputBlobPath);
    }

    /// <summary>
    /// Starts the background rendering worker (call once during app startup).
    /// </summary>
    public void StartWorker()
    {
        if (_processingTask is not null)
            return;

        _processingTask = ProcessRenderJobsAsync(_cts.Token);
    }

    private async Task ProcessRenderJobsAsync(CancellationToken cancellationToken)
    {
        await foreach (var job in _jobQueue.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ExecuteRenderJobAsync(job, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FFmpeg render failed for session {SessionId}", job.SessionId);
            }
        }
    }

    private async Task ExecuteRenderJobAsync(RenderJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting FFmpeg render for session {SessionId} with {SoundCount} sound entries",
            job.SessionId, job.SoundEntries.Count);

        // Phase 5 stub: In production, this would invoke FFmpeg with -filter_complex for:
        // - Audio replacement (-an on input, adelay + amix for sounds at job.SoundEntries[].TimestampMs)
        // - Visual effects (deep-fry, snap-zoom, motion blur, overlay) per job.SoundEntries[].VisualEffect
        // For now, log and complete
        
        await Task.Delay(100, cancellationToken); // Simulate work

        _logger.LogInformation(
            "FFmpeg render completed for session {SessionId}. Output: {Path}",
            job.SessionId, job.OutputBlobPath);
    }

    public async ValueTask DisposeAsync()
    {
        _jobQueue.Writer.Complete();
        if (_processingTask is not null)
            await _processingTask;
        _cts.Cancel();
        _cts.Dispose();
    }
}
