// SOLID: Single Responsibility — sound asset persistence isolated
using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace PoMemeVideo.Api.Features.MemeLibrary;

public sealed class SoundAssetTableRepository : ISoundAssetRepository
{
    private const string TableName = "SoundAssets";
    private const string PartitionKey = "library";
    private static readonly object CacheKey = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly AzureTableClientFactory _factory;
    private readonly IMemoryCache _cache;

    public SoundAssetTableRepository(AzureTableClientFactory factory, IMemoryCache cache)
    {
        _factory = factory;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SoundAsset>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<SoundAsset>? cached) && cached is not null)
            return cached;

        var client = _factory.GetTableClient(TableName);
        var assets = new List<SoundAsset>();

        await foreach (var entity in client.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{PartitionKey}'",
            cancellationToken: cancellationToken))
        {
            var tags = entity.GetString("Tags")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
            var embeddingCsv = entity.GetString("EmbeddingVector") ?? string.Empty;
            var embedding = embeddingCsv.Length > 0
                ? embeddingCsv.Split(',').Select(float.Parse).ToArray()
                : [];

            assets.Add(new SoundAsset
            {
                SoundId = Guid.Parse(entity.RowKey),
                DisplayName = entity.GetString("DisplayName") ?? string.Empty,
                DurationMs = entity.GetInt32("DurationMs") ?? 0,
                ActionVectorTags = tags,
                BlobUrl = entity.GetString("BlobUrl") ?? string.Empty,
                EmbeddingVector = embedding,
                Priority = entity.GetBoolean("Priority") ?? false,
                UseCase = entity.GetString("UseCase") ?? string.Empty,
            });
        }

        var result = (IReadOnlyList<SoundAsset>)assets;
        _cache.Set(CacheKey, result, new MemoryCacheEntryOptions { SlidingExpiration = CacheDuration });
        return result;
    }

    public void InvalidateCache() => _cache.Remove(CacheKey);
}

public static class SoundAssetTableRepositoryExtensions
{
    public static IServiceCollection AddSoundAssetTableRepository(this IServiceCollection services)
        => services.AddSingleton<ISoundAssetRepository, SoundAssetTableRepository>();
}
