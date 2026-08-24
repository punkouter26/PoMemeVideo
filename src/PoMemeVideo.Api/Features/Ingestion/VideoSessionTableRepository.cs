// GoF: Repository Pattern
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Api.Features.Ingestion;

internal sealed class VideoSessionTableEntity : ITableEntity
{
    /// <summary>
    /// Constant partition key. Per-user isolation is enforced by the <c>OwnerUserId</c>
    /// property (the row-level authorization filter used by every lookup), NOT by the
    /// partition key — because dev/ANON identities can rotate per request and the
    /// cookie-issued GUID is the wrong thing to hash a row on.
    /// </summary>
    public string PartitionKey { get; set; } = PartitionKeyValue;
    public string RowKey { get; set; } = string.Empty; // SessionId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string SourceBlobPath { get; set; } = string.Empty;
    public double VideoDurationSeconds { get; set; }
    public bool AggressiveVisuals { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? OutputBlobPath { get; set; }

    /// <summary>Owner of this session — used to authorize lookups.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    public const string PartitionKeyValue = "sessions";
}

public sealed class VideoSessionTableRepository : IVideoSessionRepository
{
    private const string TableName = StorageNames.Tables.VideoSessions;
    private readonly TableClient _table;

    public VideoSessionTableRepository(AzureTableClientFactory factory)
    {
        _table = factory.GetTableClient(TableName);
    }

    public async Task<VideoSession> CreateAsync(VideoSession session, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(session);
        await _table.AddEntityAsync(entity, cancellationToken);
        return session;
    }

    public async Task<VideoSession?> GetByIdAsync(SessionId sessionId, UserId userId, CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(sessionId, cancellationToken);
        if (entity is null) return null;

        // Owner check: prevents cross-user reads even though PK is constant.
        if (!IsOwner(entity, userId))
            return null;

        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<VideoSession>> ListCompletedAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var results = new List<VideoSession>();
        var filter = $"PartitionKey eq '{VideoSessionTableEntity.PartitionKeyValue}' and Status eq 'Complete' and OwnerUserId eq '{userId.Value:D}'";
        await foreach (var entity in _table.QueryAsync<VideoSessionTableEntity>(filter, cancellationToken: cancellationToken))
        {
            results.Add(ToDomain(entity));
        }
        results.Sort((a, b) => DateTimeOffset.Compare(b.CompletedAt ?? b.CreatedAt, a.CompletedAt ?? a.CreatedAt));
        return results;
    }

    public async Task UpdateMetadataAsync(
        SessionId sessionId,
        UserId userId,
        string sourceBlobPath,
        double videoDurationSeconds,
        bool aggressiveVisuals,
        CancellationToken cancellationToken = default)
    {
        var entity = await RequireOwnedAsync(sessionId, userId, cancellationToken);

        entity.SourceBlobPath = sourceBlobPath;
        entity.VideoDurationSeconds = videoDurationSeconds;
        entity.AggressiveVisuals = aggressiveVisuals;

        await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge, cancellationToken);
    }

    public async Task UpdateStatusAsync(
        SessionId sessionId,
        UserId userId,
        SessionStatus status,
        string? errorMessage = null,
        string? outputBlobPath = null,
        double? videoDurationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await RequireOwnedAsync(sessionId, userId, cancellationToken);

        entity.Status = status.ToString();
        entity.ErrorMessage = errorMessage;

        if (outputBlobPath is not null)
            entity.OutputBlobPath = outputBlobPath;

        if (videoDurationSeconds is > 0)
            entity.VideoDurationSeconds = videoDurationSeconds.Value;

        if (status is SessionStatus.Complete or SessionStatus.Error)
        {
            entity.CompletedAt = DateTimeOffset.UtcNow;
        }

        await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge, cancellationToken);
    }

    public async Task DeleteAsync(SessionId sessionId, UserId userId, CancellationToken cancellationToken = default)
    {
        var entity = await RequireOwnedAsync(sessionId, userId, cancellationToken);
        await _table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag, cancellationToken);
    }

    private async Task<VideoSessionTableEntity?> GetEntityAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.GetEntityAsync<VideoSessionTableEntity>(
                partitionKey: VideoSessionTableEntity.PartitionKeyValue,
                rowKey: sessionId.ToString(),
                cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<VideoSessionTableEntity> RequireOwnedAsync(SessionId sessionId, UserId userId, CancellationToken cancellationToken)
    {
        var entity = await GetEntityAsync(sessionId, cancellationToken)
            ?? throw new SessionNotFoundException(sessionId);

        if (!IsOwner(entity, userId))
            throw new SessionNotFoundException(sessionId);

        return entity;
    }

    private static bool IsOwner(VideoSessionTableEntity entity, UserId userId)
        => Guid.TryParse(entity.OwnerUserId, out var ownerId) && ownerId == userId.Value;

    private static VideoSessionTableEntity ToEntity(VideoSession s) => new()
    {
        RowKey = s.SessionId.ToString(),
        SourceBlobPath = s.SourceBlobPath,
        VideoDurationSeconds = s.VideoDurationSeconds,
        AggressiveVisuals = s.AggressiveVisuals,
        Status = s.Status.ToString(),
        ErrorMessage = s.ErrorMessage,
        CreatedAt = s.CreatedAt,
        CompletedAt = s.CompletedAt,
        OutputBlobPath = s.OutputBlobPath,
        OwnerUserId = s.UserId.Value.ToString("D"),
    };

    private static VideoSession ToDomain(VideoSessionTableEntity e) => new()
    {
        SessionId = new SessionId(Guid.Parse(e.RowKey)),
        UserId = Guid.TryParse(e.OwnerUserId, out var ownerId) ? new UserId(ownerId) : UserId.Empty,
        SourceBlobPath = e.SourceBlobPath,
        VideoDurationSeconds = e.VideoDurationSeconds,
        AggressiveVisuals = e.AggressiveVisuals,
        Status = Enum.Parse<SessionStatus>(e.Status),
        ErrorMessage = e.ErrorMessage,
        CreatedAt = e.CreatedAt,
        CompletedAt = e.CompletedAt,
        OutputBlobPath = e.OutputBlobPath,
    };
}

public sealed class SessionNotFoundException : Exception
{
    public SessionId SessionId { get; }

    public SessionNotFoundException(SessionId sessionId)
        : base($"Session {sessionId} not found.")
    {
        SessionId = sessionId;
    }
}

public static class VideoSessionTableRepositoryExtensions
{
    public static IServiceCollection AddVideoSessionTableRepository(this IServiceCollection services)
        => services.AddScoped<IVideoSessionRepository, VideoSessionTableRepository>();
}
