using PoMemeVideo.Api.Entities;

namespace PoMemeVideo.Api.Interfaces;

public interface ISoundAssetRepository
{
    Task<IReadOnlyList<SoundAsset>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Evicts the in-memory cache so the next LoadAllAsync re-reads from storage.</summary>
    void InvalidateCache();
}
