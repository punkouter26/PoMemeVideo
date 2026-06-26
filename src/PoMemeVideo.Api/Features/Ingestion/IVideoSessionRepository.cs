using PoMemeVideo.Api.Entities;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Api.Interfaces;

public interface IVideoSessionRepository
{
    Task<VideoSession> CreateAsync(VideoSession session, CancellationToken cancellationToken = default);
    Task<VideoSession?> GetByIdAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoSession>> ListCompletedAsync(Guid userId, CancellationToken cancellationToken = default);
    Task UpdateMetadataAsync(Guid sessionId, Guid userId, string sourceBlobPath, double videoDurationSeconds, bool aggressiveVisuals, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid sessionId, Guid userId, SessionStatus status, string? errorMessage = null, string? outputBlobPath = null, double? videoDurationSeconds = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
}
