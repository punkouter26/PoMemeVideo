namespace PoMemeVideo.Domain.Interfaces;

public interface IBlobStorageService
{
    Task<Stream> StreamBlobAsync(string path, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> ListBlobsByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    Task DeleteBlobsByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
