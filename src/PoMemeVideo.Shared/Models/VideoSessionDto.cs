using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Shared.Models;

public class VideoSessionDto
{
    public Guid SessionId { get; init; }
    public Guid UserId { get; init; }
    public string SourceBlobPath { get; init; } = string.Empty;
    public double VideoDurationSeconds { get; init; }
    public bool AggressiveVisuals { get; init; }
    public SessionStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? OutputBlobPath { get; init; }
    public double? TrimStartSeconds { get; init; }
    public double? TrimDurationSeconds { get; init; }
    public string? MemePersona { get; init; }
    public string? AspectRatio { get; init; }
}
