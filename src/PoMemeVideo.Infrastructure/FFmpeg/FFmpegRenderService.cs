using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure.AzureStorage;
using PoMemeVideo.Shared;
using System.Diagnostics;
using System.Text;
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
    private int _disposed; // 0 = live, 1 = disposed (Interlocked)

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
    /// Queues a render job and awaits its completion. Returns when FFmpeg finishes and output is uploaded.
    /// </summary>
    public async Task RenderAsync(RenderJob job, CancellationToken cancellationToken = default)
    {
        await _jobQueue.Writer.WriteAsync(job, cancellationToken);
        _logger.LogInformation(
            "FFmpeg render job queued for session {SessionId}: output → {OutputPath}",
            job.SessionId, job.OutputBlobPath);

        // Await actual FFmpeg completion — the worker signals Completion when done
        await job.Completion.Task;
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
                job.Completion.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FFmpeg render failed for session {SessionId}", job.SessionId);
                job.Completion.TrySetException(ex);
            }
        }
    }

    private async Task ExecuteRenderJobAsync(RenderJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting FFmpeg render for session {SessionId} with {SoundCount} sound(s)",
            job.SessionId, job.SoundEntries.Count);

        var tempDir = Path.Combine(Path.GetTempPath(), $"{PoMemeVideoNaming.ApplicationSlug}-{job.SessionId}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // ── 1. Download source video from blob ────────────────────────────
            var sourceExt = Path.GetExtension(job.SourceBlobPath).TrimStart('.');
            var sourcePath = Path.Combine(tempDir, $"source.{sourceExt}");
            await DownloadBlobToFileAsync(job.SourceBlobPath, sourcePath, cancellationToken);
            _logger.LogInformation("Source video downloaded: {Path}", sourcePath);

            // ── 2. Download each sound file from blob ─────────────────────────
            var soundPaths = new List<(long TimestampMs, string FilePath, string? VisualEffect, double? Intensity)>();
            for (var i = 0; i < job.SoundEntries.Count; i++)
            {
                var entry = job.SoundEntries[i];
                var soundExt = Path.GetExtension(entry.SoundBlobUrl);
                if (string.IsNullOrEmpty(soundExt)) soundExt = ".mp3";
                var soundPath = Path.Combine(tempDir, $"sound_{i}{soundExt}");

                try
                {
                    await DownloadBlobToFileAsync(entry.SoundBlobUrl, soundPath, cancellationToken);
                    soundPaths.Add((entry.TimestampMs, soundPath, entry.VisualEffect, entry.EffectIntensity));
                    _logger.LogDebug("Sound {Index} downloaded: {Path}", i, soundPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not download sound {Index} ({Url}) — skipping", i, entry.SoundBlobUrl);
                }
            }

            // ── 3. Build FFmpeg command ───────────────────────────────────────
            var outputPath = Path.Combine(tempDir, "output.mp4");
            var args = BuildFFmpegArgs(sourcePath, soundPaths, outputPath, job.AggressiveVisuals);

            _logger.LogDebug("FFmpeg args: {Args}", args);

            // ── 4. Run FFmpeg ─────────────────────────────────────────────────
            var exitCode = await RunFFmpegAsync(args, job.SessionId, cancellationToken);
            if (exitCode != 0)
                throw new InvalidOperationException($"FFmpeg exited with code {exitCode} for session {job.SessionId}.");

            _logger.LogInformation("FFmpeg render complete for session {SessionId}", job.SessionId);

            // ── 5. Upload output to blob storage ──────────────────────────────
            await UploadFileToBlobAsync(outputPath, job.OutputBlobPath, cancellationToken);
            _logger.LogInformation("Output uploaded: {Path}", job.OutputBlobPath);
        }
        finally
        {
            // ── 6. Clean up temp files ────────────────────────────────────────
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete temp dir {Dir}", tempDir); }
        }
    }

    /// <summary>
    /// GoF: Template Method — builds the -filter_complex string based on effect types.
    /// Audio: strips original audio (-an), adds each meme sound with adelay, mixes with amix.
    /// Video: chains optional deep-fry / snap-zoom / motion-blur filters.
    /// </summary>
    private static string BuildFFmpegArgs(
        string sourcePath,
        IReadOnlyList<(long TimestampMs, string FilePath, string? VisualEffect, double? Intensity)> sounds,
        string outputPath,
        bool aggressiveVisuals)
    {
        var sb = new StringBuilder();

        // Input 0: source video (no original audio)
        sb.Append($"-i \"{sourcePath}\"");

        // Inputs 1..N: sound files
        foreach (var (_, filePath, _, _) in sounds)
            sb.Append($" -i \"{filePath}\"");

        var videoChain = BuildVideoFilterChain(sounds, aggressiveVisuals);
        var hasVideoFilters = !string.IsNullOrWhiteSpace(videoChain);
        var hasAudioMix = sounds.Count > 0;

        if (hasVideoFilters || hasAudioMix)
        {
            var fc = new StringBuilder();

            if (hasVideoFilters)
                fc.Append($"[0:v]{videoChain}[vout]");

            if (hasAudioMix)
            {
                for (var i = 0; i < sounds.Count; i++)
                {
                    var delayMs = sounds[i].TimestampMs;
                    if (fc.Length > 0)
                        fc.Append(';');
                    fc.Append($"[{i + 1}:a]adelay={delayMs}|{delayMs}[a{i}]");
                }

                var mixInputs = string.Concat(Enumerable.Range(0, sounds.Count).Select(i => $"[a{i}]"));
                fc.Append($";{mixInputs}amix=inputs={sounds.Count}:normalize=0:duration=longest[aout]");
            }

            sb.Append($" -filter_complex \"{fc}\"");
        }

        // Map outputs
        sb.Append(hasVideoFilters ? " -map \"[vout]\"" : " -map 0:v");
        sb.Append(hasAudioMix ? " -map \"[aout]\"" : " -an");

        // Encoding settings: H.264 video + AAC audio, fast encode
        sb.Append(" -c:v libx264 -preset fast -crf 23");
        if (sounds.Count > 0)
            sb.Append(" -c:a aac -b:a 192k");
        // -shortest: trim output to the video stream length so audio doesn't extend past video end
        if (sounds.Count > 0)
            sb.Append(" -shortest");
        sb.Append(" -movflags +faststart");
        sb.Append($" -y \"{outputPath}\"");

        return sb.ToString();
    }

    /// <summary>
    /// Builds the video filter chain. Aggressive visuals enable deep-fry EQ + unsharp.
    /// Per-entry VisualEffect values apply to the overall output (most common effect wins).
    /// </summary>
    private static string BuildVideoFilterChain(
        IReadOnlyList<(long TimestampMs, string FilePath, string? VisualEffect, double? Intensity)> sounds,
        bool aggressiveVisuals)
    {
        // Tally which visual effects are requested
        var effectCounts = sounds
            .Where(s => s.VisualEffect is not null)
            .GroupBy(s => s.VisualEffect!)
            .ToDictionary(g => g.Key, g => g.Count());

        var filters = new List<string>();

        if (aggressiveVisuals || effectCounts.ContainsKey("DeepFry"))
        {
            // Deep-fry: saturate + sharpen
            filters.Add("eq=saturation=3:contrast=1.5:brightness=0.05");
            filters.Add("unsharp=5:5:1.5:5:5:0.0");
        }

        var snapZoomEntries = sounds.Where(s => s.VisualEffect == "SnapZoom").ToList();
        if (snapZoomEntries.Count > 0)
        {
            // SnapZoom is an audio-placement cue only — the meme sound fires at the timestamp.
            // A true per-frame zoom requires segment splicing which is not yet implemented;
            // visual effect is intentionally omitted here to avoid filter-graph errors.
        }

        if (effectCounts.ContainsKey("MotionBlur"))
        {
            // Motion blur via minterpolate (requires libopencv or tblend fallback)
            filters.Add("tblend=all_mode=average");
        }

        return filters.Count > 0 ? string.Join(',', filters) : string.Empty;
    }

    private async Task<int> RunFFmpegAsync(string args, Guid sessionId, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
                _logger.LogTrace("[FFmpeg:{SessionId}] {Line}", sessionId, e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
            _logger.LogError("FFmpeg stderr for {SessionId}:\n{Stderr}", sessionId, stderr);

        return process.ExitCode;
    }

    private async Task DownloadBlobToFileAsync(string blobPath, string destPath, CancellationToken ct)
    {
        await using var blobStream = await _blobService.StreamBlobAsync(blobPath, ct);
        await using var fileStream = File.Create(destPath);
        await blobStream.CopyToAsync(fileStream, ct);
    }

    private async Task UploadFileToBlobAsync(string filePath, string blobPath, CancellationToken ct)
    {
        // blobPath format: "sessions/{sessionId}/output.mp4" → container=sessions, blob={sessionId}/output.mp4
        var slash = blobPath.IndexOf('/');
        var container = blobPath[..slash];
        var blobName = blobPath[(slash + 1)..];

        var containerClient = _blobService.GetContainerClientPublic(container);
        var blobClient = containerClient.GetBlobClient(blobName);

        await using var stream = File.OpenRead(filePath);
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return; // already disposed — idempotent guard against double-dispose from DI

        _jobQueue.Writer.TryComplete();  // idempotent — no-op if already completed
        await _cts.CancelAsync();
        if (_processingTask is not null)
        {
            try { await _processingTask; }
            catch (OperationCanceledException) { /* expected on graceful shutdown */ }
        }
        _cts.Dispose();
    }
}

