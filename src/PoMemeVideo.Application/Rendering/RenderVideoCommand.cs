using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Shared.Enums;
using PoMemeVideo.Shared.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PoMemeVideo.Application.Rendering;

/// <summary>
/// Orchestrates video rendering: queues render job, uploads output, updates session status.
/// SOLID: Dependency Inversion — depends on IVideoRenderService abstraction.
/// GoF: Command Pattern.
/// </summary>
public class RenderVideoCommand
{
    private readonly IVideoRenderService _renderService;
    private readonly IVideoSessionRepository _sessionRepository;
    private readonly IEngineNotifier _notifier;
    private readonly ILogger<RenderVideoCommand> _logger;

    public RenderVideoCommand(
        IVideoRenderService renderService,
        IVideoSessionRepository sessionRepository,
        IEngineNotifier notifier,
        ILogger<RenderVideoCommand> logger)
    {
        _renderService = renderService;
        _sessionRepository = sessionRepository;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// Executes video rendering for a completed session.
    /// </summary>
    public async Task ExecuteAsync(
        Guid sessionId,
        Guid userId,
        VideoSession session,
        DirectorScript script,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting render for session {SessionId}", sessionId);

            // Parse script entries from JSON
            var soundEntries = new List<RenderSoundEntry>();
            try
            {
                var entries = JsonSerializer.Deserialize<List<ScriptEntryDto>>(
                    script.EntriesJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (entries != null)
                {
                    soundEntries = entries
                        .Select(entry => new RenderSoundEntry(
                            TimestampMs: entry.TimestampMs,
                            SoundBlobUrl: $"sessions/{sessionId}/sounds/{entry.SoundId}",
                            VisualEffect: entry.VisualEffect?.ToString(),
                            EffectIntensity: entry.EffectIntensity,
                            OverlayAssetId: entry.OverlayAssetId))
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
                SoundEntries: soundEntries);

            await _renderService.RenderAsync(job, cancellationToken);

            // Update session status to Complete with output path
            session.Status = SessionStatus.Complete;
            session.OutputBlobPath = job.OutputBlobPath;
            session.CompletedAt = DateTimeOffset.UtcNow;

            await _sessionRepository.UpdateStatusAsync(
                sessionId,
                userId,
                SessionStatus.Complete,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Render queued for session {SessionId}. Output: {OutputPath}",
                sessionId, job.OutputBlobPath);

            // Signal completion via IEngineNotifier
            await _notifier.CompleteAsync(sessionId, job.OutputBlobPath, cancellationToken);
        }
        catch (Exception ex)
        {
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
}
