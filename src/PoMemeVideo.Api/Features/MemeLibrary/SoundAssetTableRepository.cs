// SOLID: Single Responsibility — sound asset persistence isolated
using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace PoMemeVideo.Api.Features.MemeLibrary;

public sealed class SoundAssetTableRepository : ISoundAssetRepository
{
    private const string TableName = StorageNames.Tables.SoundAssets;
    private const string PartitionKey = "library";
    private const string CacheKey = "memelibrary:sounds:all";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly AzureTableClientFactory _factory;
    private readonly HybridCache _cache;

    public SoundAssetTableRepository(AzureTableClientFactory factory, HybridCache cache)
    {
        _factory = factory;
        _cache = cache;
    }

    /// <summary>
    /// HybridCache stampede-protects this read: concurrent callers on a cold cache share one
    /// storage round-trip instead of each issuing their own table scan.
    /// </summary>
    public async Task<IReadOnlyList<SoundAsset>> LoadAllAsync(CancellationToken cancellationToken = default)
        => await _cache.GetOrCreateAsync(
            CacheKey,
            this,
            static (repo, ct) => repo.QueryAllAsync(ct),
            new HybridCacheEntryOptions { Expiration = CacheDuration, LocalCacheExpiration = CacheDuration },
            cancellationToken: cancellationToken);

    private async ValueTask<IReadOnlyList<SoundAsset>> QueryAllAsync(CancellationToken cancellationToken)
    {
        var client = _factory.GetTableClient(TableName);
        var assets = new List<SoundAsset>();

        await foreach (var entity in client.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{PartitionKey}'",
            cancellationToken: cancellationToken))
        {
            var tags = entity.GetString("Tags")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
            assets.Add(new SoundAsset
            {
                SoundId = new SoundId(Guid.Parse(entity.RowKey)),
                DisplayName = entity.GetString("DisplayName") ?? string.Empty,
                DurationMs = entity.GetInt32("DurationMs") ?? 0,
                ActionVectorTags = tags,
                BlobUrl = entity.GetString("BlobUrl") ?? string.Empty,
                Priority = entity.GetBoolean("Priority") ?? false,
                UseCase = entity.GetString("UseCase") ?? string.Empty,
            });
        }

        return assets;
    }

    public async Task AddSoundAsync(SoundAsset asset, CancellationToken cancellationToken = default)
    {
        var client = _factory.GetTableClient(TableName);
        var entity = new TableEntity(PartitionKey, asset.SoundId.ToString())
        {
            ["DisplayName"] = asset.DisplayName,
            ["DurationMs"] = asset.DurationMs,
            ["Tags"] = string.Join(",", asset.ActionVectorTags),
            ["BlobUrl"] = asset.BlobUrl,
            ["Priority"] = asset.Priority,
            ["UseCase"] = asset.UseCase
        };
        await client.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        InvalidateCache();
    }

    /// <remarks>Fire-and-forget: the contract is synchronous and eviction is not ordered work.</remarks>
    public void InvalidateCache() => _ = _cache.RemoveAsync(CacheKey).AsTask();
}

public static class SoundAssetTableRepositoryExtensions
{
    public static IServiceCollection AddSoundAssetTableRepository(this IServiceCollection services)
        => services.AddSingleton<ISoundAssetRepository, SoundAssetTableRepository>();
}
