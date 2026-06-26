// GoF: Entity
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Domain.Entities;

public class VideoSession
{
    public Guid SessionId { get; init; } = Guid.NewGuid();
    public required Guid UserId { get; init; }
    public required string SourceBlobPath { get; set; }
    public double VideoDurationSeconds { get; set; }
    public bool AggressiveVisuals { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Ingesting;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? OutputBlobPath { get; set; }
}
