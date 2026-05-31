// GoF: Repository Pattern
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using PoMemeVideo.Domain.Entities;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Infrastructure.AzureStorage;

internal sealed class VideoSessionTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // UserId
    public string RowKey { get; set; } = string.Empty;       // SessionId
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
}

public sealed class VideoSessionTableRepository : IVideoSessionRepository
{
    private const string TableName = "VideoSessions";
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

    public async Task<VideoSession?> GetByIdAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<VideoSessionTableEntity>(
                partitionKey: userId.ToString(),
                rowKey: sessionId.ToString(),
                cancellationToken: cancellationToken);
            return ToDomain(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<VideoSession>> ListCompletedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var results = new List<VideoSession>();
        var filter = $"PartitionKey eq '{userId}' and Status eq 'Complete'";
        await foreach (var entity in _table.QueryAsync<VideoSessionTableEntity>(filter, cancellationToken: cancellationToken))
        {
            results.Add(ToDomain(entity));
        }
        results.Sort((a, b) => DateTimeOffset.Compare(b.CompletedAt ?? b.CreatedAt, a.CompletedAt ?? a.CreatedAt));
        return results;
    }

    public async Task UpdateMetadataAsync(
        Guid sessionId,
        Guid userId,
        string sourceBlobPath,
        double videoDurationSeconds,
        bool aggressiveVisuals,
        CancellationToken cancellationToken = default)
    {
        var response = await _table.GetEntityAsync<VideoSessionTableEntity>(
            partitionKey: userId.ToString(),
            rowKey: sessionId.ToString(),
            cancellationToken: cancellationToken);

        var entity = response.Value;
        entity.SourceBlobPath = sourceBlobPath;
        entity.VideoDurationSeconds = videoDurationSeconds;
        entity.AggressiveVisuals = aggressiveVisuals;

        await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge, cancellationToken);
    }

    public async Task UpdateStatusAsync(
        Guid sessionId,
        Guid userId,
        SessionStatus status,
        string? errorMessage = null,
        string? outputBlobPath = null,
        double? videoDurationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _table.GetEntityAsync<VideoSessionTableEntity>(
            partitionKey: userId.ToString(),
            rowKey: sessionId.ToString(),
            cancellationToken: cancellationToken);

        var entity = response.Value;
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

    public async Task DeleteAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        await _table.DeleteEntityAsync(
            partitionKey: userId.ToString(),
            rowKey: sessionId.ToString(),
            cancellationToken: cancellationToken);
    }

    private static VideoSessionTableEntity ToEntity(VideoSession s) => new()
    {
        PartitionKey = s.UserId.ToString(),
        RowKey = s.SessionId.ToString(),
        SourceBlobPath = s.SourceBlobPath,
        VideoDurationSeconds = s.VideoDurationSeconds,
        AggressiveVisuals = s.AggressiveVisuals,
        Status = s.Status.ToString(),
        ErrorMessage = s.ErrorMessage,
        CreatedAt = s.CreatedAt,
        CompletedAt = s.CompletedAt,
        OutputBlobPath = s.OutputBlobPath,
    };

    private static VideoSession ToDomain(VideoSessionTableEntity e) => new()
    {
        SessionId = Guid.Parse(e.RowKey),
        UserId = Guid.Parse(e.PartitionKey),
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

public static class VideoSessionTableRepositoryExtensions
{
    public static IServiceCollection AddVideoSessionTableRepository(this IServiceCollection services)
        => services.AddScoped<IVideoSessionRepository, VideoSessionTableRepository>();
}
