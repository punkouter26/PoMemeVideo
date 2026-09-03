using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoMemeVideo.Shared;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace PoMemeVideo.Api.Features.Output;

/// <summary>
/// FFmpeg-based video rendering service implementing IVideoRenderService.
/// Uses System.Threading.Channels for bounded job concurrency.
/// GoF: Template Method — filter_complex construction varies per effect type.
/// </summary>
public partial class FFmpegRenderService : IVideoRenderService, IAsyncDisposable
{
    [LoggerMessage(Level = LogLevel.Information, Message = "FFmpeg render complete for session {SessionId}")]
    private partial void LogRenderComplete(SessionId sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "ffprobe: session {SessionId} actual output duration = {Duration:F2}s")]
    private partial void LogProbeDuration(SessionId sessionId, double duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ffprobe duration unavailable for session {SessionId}")]
    private partial void LogProbeUnavailable(SessionId sessionId);

    /// <summary>Wall-clock ceiling for a single ffmpeg render before it is aborted as failed.</summary>
    private const int RenderTimeoutMinutes = 5;

    private readonly BlobStorageService _blobService;
    private readonly ILogger<FFmpegRenderService> _logger;
    private readonly Channel<RenderJob> _jobQueue;
    private readonly string? _ffmpegBinPath;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private int _disposed; // 0 = live, 1 = disposed (Interlocked)

    public FFmpegRenderService(BlobStorageService blobService, ILogger<FFmpegRenderService> logger, IConfiguration configuration)
    {
        _blobService = blobService;
        _logger = logger;
        _ffmpegBinPath = configuration["FFmpegBinPath"] ?? ResolveBundledBinPath();
        if (!string.IsNullOrWhiteSpace(_ffmpegBinPath))
        {
            _logger.LogInformation("FFmpegRenderService: using FFmpegBinPath = {Path}", _ffmpegBinPath);
            EnsureExecutable(_ffmpegBinPath);
        }
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

            // Probe the true source duration so the render can be hard-trimmed to match it
            // (prevents trailing meme audio from extending the output past the video).
            var sourceDurationSeconds = await ProbeOutputDurationAsync(sourcePath, cancellationToken);

            // Probe for an audio stream so we only try to mix the original audio when it exists
            // (referencing [0:a] on a silent video would fail the filter graph).
            var sourceHasAudio = await ProbeHasAudioStreamAsync(sourcePath, cancellationToken);

            // ── 2. Download each sound file from blob ─────────────────────────
            var soundPaths = new List<(long TimestampMs, string FilePath, string? VisualEffect, double? Intensity, string? CaptionText, string? CaptionPosition)>();
            for (var i = 0; i < job.SoundEntries.Count; i++)
            {
                var entry = job.SoundEntries[i];
                var soundExt = Path.GetExtension(entry.SoundBlobUrl);
                if (string.IsNullOrEmpty(soundExt)) soundExt = ".mp3";
                var soundPath = Path.Combine(tempDir, $"sound_{i}{soundExt}");

                try
                {
                    await DownloadBlobToFileAsync(entry.SoundBlobUrl, soundPath, cancellationToken);
                    if (!await ProbeHasAudioStreamAsync(soundPath, cancellationToken))
                    {
                        _logger.LogWarning("Sound {Index} ({Path}) has no valid audio stream — skipping", i, soundPath);
                        continue;
                    }
                    soundPaths.Add((entry.TimestampMs, soundPath, entry.VisualEffect, entry.EffectIntensity, entry.CaptionText, entry.CaptionPosition));
                    _logger.LogDebug("Sound {Index} downloaded: {Path}", i, soundPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not download sound {Index} ({Url}) — skipping", i, entry.SoundBlobUrl);
                }
            }

            // Effective output duration respects trimming if specified
            var effectiveDuration = job.TrimDurationSeconds.HasValue && job.TrimDurationSeconds.Value > 0
                ? job.TrimDurationSeconds.Value
                : sourceDurationSeconds;

            // ── 3. Build FFmpeg command ───────────────────────────────────────
            var outputPath = Path.Combine(tempDir, "output.mp4");
            var args = BuildFFmpegArgs(
                sourcePath,
                soundPaths,
                outputPath,
                job.AggressiveVisuals,
                effectiveDuration,
                sourceHasAudio,
                job.TrimStartSeconds,
                job.AspectRatio);

            _logger.LogDebug("FFmpeg args: {Args}", args);

            // ── 4. Run FFmpeg ─────────────────────────────────────────────────
            var exitCode = await RunFFmpegAsync(args, job.SessionId, cancellationToken);
            if (exitCode != 0)
                throw new InvalidOperationException($"FFmpeg exited with code {exitCode} for session {job.SessionId}.");

            LogRenderComplete(job.SessionId);
            // ── 4b. Probe actual output duration via ffprobe ─────────────────────────────
            job.ActualDurationSeconds = await ProbeOutputDurationAsync(outputPath, cancellationToken);
            if (job.ActualDurationSeconds > 0)
                LogProbeDuration(job.SessionId, job.ActualDurationSeconds);
            else
                LogProbeUnavailable(job.SessionId);
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
    /// Audio: keeps the original video audio and layers each meme sound on top (adelay + amix),
    /// with a limiter to prevent clipping. The original track sits slightly under the sounds so
    /// the effects still cut through. Falls back gracefully when the source has no audio stream.
    /// Video: chains optional deep-fry / snap-zoom / motion-blur filters.
    /// </summary>
    private static string BuildFFmpegArgs(
        string sourcePath,
        IReadOnlyList<(long TimestampMs, string FilePath, string? VisualEffect, double? Intensity, string? CaptionText, string? CaptionPosition)> sounds,
        string outputPath,
        bool aggressiveVisuals,
        double sourceDurationSeconds,
        bool sourceHasAudio,
        double? trimStartSeconds = null,
        string? aspectRatio = null)
    {
        var sb = new StringBuilder();

        // Input 0: source video (with optional input seeking for trimming)
        if (trimStartSeconds.HasValue && trimStartSeconds.Value > 0)
        {
            sb.Append($"-ss {trimStartSeconds.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} ");
        }
        sb.Append($"-i \"{sourcePath}\"");

        // Inputs 1..N: sound files
        foreach (var (_, filePath, _, _, _, _) in sounds)
            sb.Append($" -i \"{filePath}\"");

        var videoChain = BuildVideoFilterChain(sounds, aggressiveVisuals, aspectRatio);
        var hasVideoFilters = !string.IsNullOrWhiteSpace(videoChain);
        // Build a mixed audio track whenever there are meme sounds. The original audio is layered
        // in as an extra amix input when the source has one. With no meme sounds we keep the
        // original audio as-is; with neither, the output is silent.
        var buildAudioMix = sounds.Count > 0;

        if (hasVideoFilters || buildAudioMix)
        {
            var fc = new StringBuilder();

            if (hasVideoFilters)
                fc.Append($"[0:v]{videoChain}[vout]");

            if (buildAudioMix)
            {
                // Original audio sits slightly under the meme sounds so the effects cut through.
                if (sourceHasAudio)
                {
                    if (fc.Length > 0)
                        fc.Append(';');
                    fc.Append("[0:a]volume=0.85[aorig]");
                }

                for (var i = 0; i < sounds.Count; i++)
                {
                    var delayMs = sounds[i].TimestampMs;
                    if (fc.Length > 0)
                        fc.Append(';');
                    fc.Append($"[{i + 1}:a]adelay={delayMs}|{delayMs}[a{i}]");
                }

                var labels = new List<string>();
                if (sourceHasAudio)
                    labels.Add("[aorig]");
                labels.AddRange(Enumerable.Range(0, sounds.Count).Select(i => $"[a{i}]"));

                var mixInputs = string.Concat(labels);
                // normalize=0 keeps each source at full level; alimiter tames the clipping that
                // summing the original track with overlapping sounds would otherwise cause.
                fc.Append($";{mixInputs}amix=inputs={labels.Count}:normalize=0:duration=longest,alimiter=limit=0.95[aout]");
            }

            sb.Append($" -filter_complex \"{fc}\"");
        }

        // Map outputs
        sb.Append(hasVideoFilters ? " -map \"[vout]\"" : " -map 0:v");
        if (buildAudioMix)
            sb.Append(" -map \"[aout]\"");
        else if (sourceHasAudio)
            sb.Append(" -map 0:a");   // no meme sounds — keep the original audio untouched
        else
            sb.Append(" -an");

        // Encoding settings: H.264 video + AAC audio. 'veryfast' trades a little size for a large
        // speedup — important on the constrained B1 host where slower presets stall the render.
        sb.Append(" -c:v libx264 -preset veryfast -crf 23");
        if (buildAudioMix || sourceHasAudio)
            // -ac 2 forces stereo output. Without it amix produces the same channel count as the
            // first input (often 5.1 from phone videos) and Chromium silently fails to decode the
            // audio track, so the resulting MP4 has zero sound when played in <video>.
            sb.Append(" -c:a aac -b:a 192k -ac 2");
        // Cap the output to the source video's duration. amix=duration=longest stretches the
        // mixed audio to the longest *sound clip* (after its adelay), so a meme sound placed
        // near the end can run past the video — leaving the output longer than the source with
        // audio still playing after the picture ends. -t hard-trims both streams to the source
        // length; audio shorter than the video simply ends in silence (the video governs).
        if (sourceDurationSeconds > 0)
            sb.Append($" -t {sourceDurationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        sb.Append(" -movflags +faststart");
        sb.Append($" -y \"{outputPath}\"");

        return sb.ToString();
    }

    /// <summary>
    /// Builds the video filter chain. Always downscales to ≤720p so heavy 1080p/4K phone clips
    /// encode in reasonable time on constrained hosts. Aggressive visuals enable deep-fry EQ +
    /// unsharp. Aspect ratio 9:16 adds vertical framing. Captions are overlaid using drawtext.
    /// </summary>
    // internal (not private) so the filter-chain construction can be unit-tested directly;
    // it is pure and is the highest-risk string building in the render path.
    internal static string BuildVideoFilterChain(
        IReadOnlyList<(long TimestampMs, string FilePath, string? VisualEffect, double? Intensity, string? CaptionText, string? CaptionPosition)> sounds,
        bool aggressiveVisuals,
        string? aspectRatio = null)
    {
        // Tally which visual effects are requested
        var effectCounts = sounds
            .Where(s => s.VisualEffect is not null)
            .GroupBy(s => s.VisualEffect!)
            .ToDictionary(g => g.Key, g => g.Count());

        var filters = new List<string>();

        if (string.Equals(aspectRatio, "9:16", StringComparison.OrdinalIgnoreCase))
        {
            // Vertical 9:16 framing: scale down to fit inside 720x1280 then pad with black bars
            filters.Add("scale=720:1280:force_original_aspect_ratio=decrease,pad=720:1280:(ow-iw)/2:(oh-ih)/2:black");
        }
        else if (string.Equals(aspectRatio, "1:1", StringComparison.OrdinalIgnoreCase))
        {
            // Square 1:1 framing: scale down to fit inside 720x720 then pad
            filters.Add("scale=720:720:force_original_aspect_ratio=decrease,pad=720:720:(ow-iw)/2:(oh-ih)/2:black");
        }
        else
        {
            // Default: Cap height at 720 (never upscale) before any other filter.
            // -2 keeps width even (libx264 needs it) and preserves the aspect ratio.
            filters.Add("scale=-2:min(720\\,ih)");
        }

        if (aggressiveVisuals || effectCounts.ContainsKey("DeepFry"))
        {
            // Deep-fry: saturate + sharpen
            filters.Add("eq=saturation=3:contrast=1.5:brightness=0.05");
            filters.Add("unsharp=5:5:1.5:5:5:0.0");
        }

        if (effectCounts.ContainsKey("MotionBlur"))
        {
            // Motion blur via tblend fallback
            filters.Add("tblend=all_mode=average");
        }

        // Add text captions / meme punchline overlays
        var fontArg = ResolveFontArg();
        foreach (var s in sounds)
        {
            if (string.IsNullOrWhiteSpace(s.CaptionText)) continue;
            var sanitized = SanitizeForDrawtext(s.CaptionText.Trim().ToUpperInvariant());
            var startSec = (s.TimestampMs / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var endSec = (s.TimestampMs / 1000.0 + 2.5).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var yPos = s.CaptionPosition?.ToLowerInvariant() switch
            {
                "top" => "40",
                "center" => "(h-text_h)/2",
                _ => "h-text_h-50"
            };
            filters.Add($"drawtext=text='{sanitized}'{fontArg}:fontsize=36:fontcolor=white:borderw=3:bordercolor=black:x=(w-text_w)/2:y={yPos}:enable='between(t\\,{startSec}\\,{endSec})'");
        }

        return string.Join(',', filters);
    }

    internal static string SanitizeForDrawtext(string text)
    {
        // drawtext wraps the text in single quotes, so a literal apostrophe inside the caption
        // closes the string early (regression: "THAT DIDN'T HIT" became "THAT DIDN" + "T HIT'…" and
        // FFmpeg errored with EINVAL). Within a single-quoted arg FFmpeg does not honour the
        // C-style backslash escape sequence, so we drop apostrophes entirely. Colon is escaped
        // because it's the option delimiter, percent to avoid format-string expansion, and
        // backslash doubled because the drawtext option parser still treats it specially.
        return text
            .Replace("\\", "\\\\")
            .Replace("'", string.Empty)
            .Replace(":", @"\:")
            .Replace("%", @"\%")
            .Replace("\n", " ")
            .Replace("\r", "");
    }

    private static string ResolveFontArg()
    {
        if (OperatingSystem.IsWindows() && File.Exists(@"C:\Windows\Fonts\impact.ttf"))
            return ":fontfile='C\\:/Windows/Fonts/impact.ttf'";
        if (OperatingSystem.IsWindows() && File.Exists(@"C:\Windows\Fonts\arial.ttf"))
            return ":fontfile='C\\:/Windows/Fonts/arial.ttf'";
        if (File.Exists("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"))
            return ":fontfile='/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'";
        return string.Empty;
    }

    public async Task<string> RenderGifAsync(string mp4Path, SessionId sessionId, CancellationToken cancellationToken)
    {
        var tempGif = Path.Combine(Path.GetTempPath(), $"{PoMemeVideoNaming.ApplicationSlug}-{sessionId}-export.gif");
        var args = $"-i \"{mp4Path}\" -vf \"fps=15,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -y \"{tempGif}\"";
        var exitCode = await RunFFmpegAsync(args, sessionId, cancellationToken);
        if (exitCode != 0 || !File.Exists(tempGif))
            throw new InvalidOperationException($"FFmpeg GIF export failed with exit code {exitCode}.");
        return tempGif;
    }

    public async Task<string> RenderPunchlineClipAsync(string mp4Path, SessionId sessionId, double startSeconds, double durationSeconds, CancellationToken cancellationToken)
    {
        var tempClip = Path.Combine(Path.GetTempPath(), $"{PoMemeVideoNaming.ApplicationSlug}-{sessionId}-punchline.mp4");
        var args = $"-ss {startSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} -i \"{mp4Path}\" -t {durationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} -c:v libx264 -preset veryfast -c:a aac -y \"{tempClip}\"";
        var exitCode = await RunFFmpegAsync(args, sessionId, cancellationToken);
        if (exitCode != 0 || !File.Exists(tempClip))
            throw new InvalidOperationException($"FFmpeg punchline clip export failed with exit code {exitCode}.");
        return tempClip;
    }

    private async Task<int> RunFFmpegAsync(string args, SessionId sessionId, CancellationToken cancellationToken)
    {
        var psi = BuildPsi("ffmpeg", args);
        psi.RedirectStandardError = true;

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

        // Hard timeout so a pathologically slow or stuck encode fails cleanly (→ session Error,
        // SignalR error) instead of leaving the engine page hanging on "AI Directing…" forever.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(RenderTimeoutMinutes));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            // Distinguish our render timeout from a genuine host-shutdown cancellation: the former
            // is a real failure the user should see; the latter is an expected teardown.
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("FFmpeg render exceeded {Minutes}-minute limit for {SessionId}; aborted.\n{Stderr}",
                    RenderTimeoutMinutes, sessionId, stderr);
                throw new TimeoutException(
                    $"Render exceeded the {RenderTimeoutMinutes}-minute limit and was aborted. " +
                    "The source video may be too large/high-resolution for the current host.");
            }

            throw;
        }

        if (process.ExitCode != 0)
            _logger.LogError("FFmpeg stderr for {SessionId}:\n{Stderr}", sessionId, stderr);

        return process.ExitCode;
    }

    /// <summary>Returns true when the file has at least one audio stream (ffprobe).</summary>
    private async Task<bool> ProbeHasAudioStreamAsync(string path, CancellationToken ct)
    {
        try
        {
            var psi = BuildPsi("ffprobe",
                $"-v error -select_streams a -show_entries stream=index -of csv=p=0 \"{path}\"");

            using var probe = Process.Start(psi);
            if (probe is null) return false;

            var output = await probe.StandardOutput.ReadToEndAsync(ct);
            await probe.WaitForExitAsync(ct);

            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            // Non-critical — assume no audio so the render still succeeds (sounds only).
            return false;
        }
    }

    private async Task<double> ProbeOutputDurationAsync(string outputPath, CancellationToken ct)
    {
        try
        {
            var psi = BuildPsi("ffprobe",
                $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{outputPath}\"");

            using var probe = Process.Start(psi);
            if (probe is null) return 0;

            var durText = await probe.StandardOutput.ReadToEndAsync(ct);
            await probe.WaitForExitAsync(ct);

            if (double.TryParse(durText.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var dur) && dur > 0)
                return dur;
        }
        catch
        {
            // Non-critical — duration will remain 0
        }

        return 0;
    }

    /// <summary>
    /// Locates the static ffmpeg/ffprobe bundled into the publish output under <c>ffmpeg/</c>.
    /// The ZIP deploy ships those binaries precisely so the app does not need a container image
    /// with ffmpeg installed system-wide — that container requirement is what forced the plan up
    /// to B1. Returns null when the directory is absent (dev machines and the Docker image both
    /// have ffmpeg on PATH), leaving the bare-name lookup in <see cref="BuildPsi"/> in charge.
    /// </summary>
    private static string? ResolveBundledBinPath()
    {
        var binDir = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return File.Exists(Path.Combine(binDir, exeName)) ? binDir : null;
    }

    /// <summary>
    /// Restores the Unix executable bit on the bundled binaries. ZIP deployment is not a reliable
    /// carrier for file modes — Kudu's extraction can drop them — and a non-executable ffmpeg
    /// surfaces only at Process.Start as a bare "Permission denied" with no hint at the cause.
    /// Idempotent, and a no-op when the package arrived with its modes intact.
    /// </summary>
    private void EnsureExecutable(string binDir)
    {
        if (OperatingSystem.IsWindows())
            return;

        const UnixFileMode executeBits =
            UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        foreach (var exeName in new[] { "ffmpeg", "ffprobe" })
        {
            var path = Path.Combine(binDir, exeName);
            if (!File.Exists(path))
                continue;

            try
            {
                var mode = File.GetUnixFileMode(path);
                if ((mode & executeBits) != executeBits)
                    File.SetUnixFileMode(path, mode | executeBits);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Read-only wwwroot (e.g. WEBSITE_RUN_FROM_PACKAGE=1). Renders will fail if the
                // package did not already carry the bit, so say so loudly rather than throwing
                // here and taking down startup for an app that may never render.
                _logger.LogWarning(ex, "Could not mark {Path} executable; renders may fail", path);
            }
        }
    }

    /// <summary>Builds a ProcessStartInfo, using the full exe path when FFmpegBinPath is configured.</summary>
    private ProcessStartInfo BuildPsi(string fileName, string arguments)
    {
        // When FFmpegBinPath is configured, resolve the full path to the executable so that
        // Windows can find it regardless of the current process's PATH environment variable.
        string resolvedFileName = fileName;
        if (!string.IsNullOrWhiteSpace(_ffmpegBinPath))
        {
            var exeName = OperatingSystem.IsWindows() ? fileName + ".exe" : fileName;
            var fullPath = Path.Combine(_ffmpegBinPath, exeName);
            if (File.Exists(fullPath))
                resolvedFileName = fullPath;
        }

        return new ProcessStartInfo
        {
            FileName = resolvedFileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private async Task DownloadBlobToFileAsync(string blobPath, string destPath, CancellationToken ct)
    {
        await using var blobStream = await _blobService.StreamBlobAsync(blobPath, ct);
        await using var fileStream = File.Create(destPath);
        await blobStream.CopyToAsync(fileStream, ct);
    }

    private async Task UploadFileToBlobAsync(string filePath, string blobPath, CancellationToken ct)
    {
        await _blobService.UploadFileAsync(blobPath, filePath, "video/mp4", ct);
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

