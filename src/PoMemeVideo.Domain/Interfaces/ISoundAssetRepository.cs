using PoMemeVideo.Domain.Entities;

namespace PoMemeVideo.Domain.Interfaces;

public interface ISoundAssetRepository
{
    Task<IReadOnlyList<SoundAsset>> LoadAllAsync(CancellationToken cancellationToken = default);
}
