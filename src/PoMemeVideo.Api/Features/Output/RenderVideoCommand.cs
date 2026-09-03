using PoMemeVideo.Shared.Enums;
using PoMemeVideo.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.Features.Output;

/// <summary>
/// Orchestrates video rendering: queues render job, uploads output, updates session status.
/// SOLID: Dependency Inversion — depends on IVideoRenderService abstraction.
/// GoF: Command Pattern.
/// </summary>
public class RenderVideoCommand : IRenderVideoCommand
{
    private readonly IVideoRenderService _renderService;
    private readonly IVideoSessionRepository _sessionRepository;
    private readonly ISoundAssetRepository _sounds;
    private readonly IEngineNotifier _notifier;
    private readonly IBlobStorageService _blobs;
    private readonly ILogger<RenderVideoCommand> _logger;

    public RenderVideoCommand(
        IVideoRenderService renderService,
        IVideoSessionRepository sessionRepository,
        ISoundAssetRepository sounds,
        IEngineNotifier notifier,
        IBlobStorageService blobs,
        ILogger<RenderVideoCommand> logger)
    {
        _renderService = renderService;
        _sessionRepository = sessionRepository;
        _sounds = sounds;
        _notifier = notifier;
        _blobs = blobs;
        _logger = logger;
    }

    /// <summary>
    /// Executes video rendering for a completed session.
    /// </summary>
    public async Task ExecuteAsync(
        SessionId sessionId,
        UserId userId,
        VideoSession session,
        DirectorScript script,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting render for session {SessionId}", sessionId);

            if (string.IsNullOrEmpty(session.SourceBlobPath))
                throw new InvalidOperationException($"Session {sessionId} has no source video — upload a video before initiating rendering.");

            if (!await _blobs.BlobExistsAsync(session.SourceBlobPath, cancellationToken))
                throw new InvalidOperationException($"Source video blob not found for session {sessionId}. The video file may have been deleted or the upload did not complete.");

            // Parse script entries from JSON
            _logger.LogDebug("EntriesJson for session {SessionId}: {Json}", sessionId, script.EntriesJson);
            var soundEntries = new List<RenderSoundEntry>();
            try
            {
                var entries = JsonSerializer.Deserialize<List<ScriptEntryDto>>(
                    script.EntriesJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new JsonStringEnumConverter() }
                    });

                if (entries != null)
                {
                    // Load sound library once to resolve SoundId → BlobUrl
                    var allSounds = await _sounds.LoadAllAsync(cancellationToken);
                    // Keyed by raw Guid: entries are rehydrated from persisted JSON DTOs, which
                    // carry the wire representation of the id.
                    var soundMap = allSounds.ToDictionary(s => s.SoundId.Value, s => s.BlobUrl);

                    soundEntries = entries
                        .Select(entry =>
                        {
                            // Resolve the actual blob URL; fall back to constructed path if not found
                            soundMap.TryGetValue(entry.SoundId, out var blobUrl);
                            // Convert full Azurite URL to relative path: sounds/{filename}
                            var blobPath = blobUrl is not null
                                ? ExtractBlobPath(blobUrl)
                                : $"sessions/{sessionId}/sounds/{entry.SoundId}";
                            return new RenderSoundEntry(
                                TimestampMs: entry.TimestampMs,
                                SoundBlobUrl: blobPath,
                                VisualEffect: entry.VisualEffect?.ToString(),
                                EffectIntensity: entry.EffectIntensity,
                                OverlayAssetId: entry.OverlayAssetId,
                                CaptionText: entry.CaptionText,
                                CaptionPosition: entry.CaptionPosition);
                        })
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse script entries for session {SessionId}", sessionId);
            }

            // Queue render job with FFmpeg service
            var job = new RenderJob(
                SessionId: sessionId,
                SourceBlobPath: session.SourceBlobPath,
                OutputBlobPath: $"sessions/{sessionId}/output.mp4",
                AggressiveVisuals: session.AggressiveVisuals,
                SoundEntries: soundEntries,
                TrimStartSeconds: session.TrimStartSeconds,
                TrimDurationSeconds: session.TrimDurationSeconds,
                AspectRatio: session.AspectRatio);

            await _renderService.RenderAsync(job, cancellationToken);

            // Update session status to Complete with output path
            session.Status = SessionStatus.Complete;
            session.OutputBlobPath = job.OutputBlobPath;
            session.CompletedAt = DateTimeOffset.UtcNow;

            await _sessionRepository.UpdateStatusAsync(
                sessionId,
                userId,
                SessionStatus.Complete,
                outputBlobPath: job.OutputBlobPath,
                videoDurationSeconds: job.ActualDurationSeconds > 0 ? job.ActualDurationSeconds : null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Render complete for session {SessionId}. Output: {OutputPath}",
                sessionId, job.OutputBlobPath);

            // Signal completion via IEngineNotifier
            await _notifier.CompleteAsync(sessionId, job.OutputBlobPath, cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
                _logger.LogWarning("Render cancelled for session {SessionId} (host shutdown or test teardown).", sessionId);
            else
                _logger.LogError(ex, "Render command failed for session {SessionId}", sessionId);

            // Update session with error status
            session.Status = SessionStatus.Error;
            session.ErrorMessage = $"Render failed: {ex.Message}";

            try
            {
                await _sessionRepository.UpdateStatusAsync(
                    sessionId,
                    userId,
                    SessionStatus.Error,
                    cancellationToken: cancellationToken);

                await _notifier.ErrorAsync(sessionId, ex.Message, cancellationToken);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Error updating session status during render failure");
            }

            throw;
        }
    }

    /// <summary>
    /// Converts a full Azurite/Azure blob URL to a relative path (container/blobName)
    /// that BlobStorageService.StreamBlobAsync understands.
    /// e.g. "http://127.0.0.1:10000/devstoreaccount1/sounds/vine-boom.mp3" → "sounds/vine-boom.mp3"
    /// </summary>
    private static string ExtractBlobPath(string blobUrl)
    {
        // Azure Blob URL format: https://{account}.blob.core.windows.net/{container}/{blob}
        // Azurite URL format:    http://127.0.0.1:10000/devstoreaccount1/{container}/{blob}
        try
        {
            var uri = new Uri(blobUrl);
            var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            // Azurite has an extra account segment; Azure doesn't
            if (segments.Length == 2 && segments[0].StartsWith("devstoreaccount", StringComparison.OrdinalIgnoreCase))
                return segments[1]; // "sounds/vine-boom.mp3"
            if (segments.Length >= 1)
                return string.Join('/', segments);
        }
        catch { /* fall through */ }
        return blobUrl;
    }
}
