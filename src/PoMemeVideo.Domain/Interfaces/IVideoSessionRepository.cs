using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Domain.Interfaces;

public interface IVideoSessionRepository
{
    Task<VideoSession> CreateAsync(VideoSession session, CancellationToken cancellationToken = default);
    Task<VideoSession?> GetByIdAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid sessionId, Guid userId, SessionStatus status, string? errorMessage = null, string? outputBlobPath = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
}
