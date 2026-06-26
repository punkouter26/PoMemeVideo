namespace PoMemeVideo.Api.Features.Output;

public interface IBlobStorageService
{
    Task<Stream> StreamBlobAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken = default);
    Task UploadBlobAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> ListBlobsByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    Task DeleteBlobsByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
