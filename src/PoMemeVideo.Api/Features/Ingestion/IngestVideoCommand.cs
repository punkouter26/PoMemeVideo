// SOLID: Single Responsibility — validation separated from persistence
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Api.Features.Ingestion;

public sealed record IngestVideoResult(Guid SessionId, string SourceBlobPath);

public sealed class IngestVideoCommand
{
    public static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".avi", ".webm" };

    public const long MaxFileSizeBytes = 500L * 1024 * 1024; // 500 MB

    private readonly IVideoSessionRepository _repository;

    public IngestVideoCommand(IVideoSessionRepository repository)
    {
        _repository = repository;
    }

    public async Task<IngestVideoResult> ExecuteAsync(
        string fileName,
        long fileSizeBytes,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!AllowedExtensions.Contains(extension))
        {
            throw new VideoIngestionValidationException(
                $"Unsupported file extension '{extension}'. Allowed: {string.Join(", ", AllowedExtensions)}",
                "INVALID_EXTENSION");
        }

        if (fileSizeBytes > MaxFileSizeBytes)
        {
            throw new VideoIngestionValidationException(
                $"File size {fileSizeBytes} bytes exceeds maximum of {MaxFileSizeBytes} bytes (500 MB).",
                "FILE_TOO_LARGE",
                MaxFileSizeBytes,
                fileSizeBytes);
        }

        // Generate session ID upfront so it can be used in the blob path
        var sessionId = Guid.NewGuid();

        var session = new VideoSession
        {
            SessionId = sessionId,
            UserId = userId,
            SourceBlobPath = $"sessions/{sessionId}/source{extension}",
            Status = SessionStatus.Ingesting,
        };

        await _repository.CreateAsync(session, cancellationToken);

        return new IngestVideoResult(session.SessionId, session.SourceBlobPath);
    }
}

public sealed class VideoIngestionValidationException : Exception
{
    public string ErrorCode { get; }
    public long? MaxBytes { get; }
    public long? ReceivedBytes { get; }

    public VideoIngestionValidationException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public VideoIngestionValidationException(string message, string errorCode, long maxBytes, long receivedBytes)
        : base(message)
    {
        ErrorCode = errorCode;
        MaxBytes = maxBytes;
        ReceivedBytes = receivedBytes;
    }
}
