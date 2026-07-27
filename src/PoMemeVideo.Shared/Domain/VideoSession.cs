// GoF: Entity
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Shared.Domain;

public class VideoSession
{
    public SessionId SessionId { get; init; } = SessionId.New();
    public required UserId UserId { get; init; }
    public required string SourceBlobPath { get; set; }
    public double VideoDurationSeconds { get; set; }
    public bool AggressiveVisuals { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Ingesting;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? OutputBlobPath { get; set; }
}
