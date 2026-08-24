// SOLID: Single Responsibility — all blob I/O isolated here
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace PoMemeVideo.Api.Features.Output;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClientFactory _factory;

    public BlobStorageService(BlobServiceClientFactory factory)
    {
        _factory = factory;
    }

    public async Task<Stream> StreamBlobAsync(string path, CancellationToken cancellationToken = default)
    {
        var (containerName, blobName) = SplitPath(path);
        var blobClient = _factory.GetContainerClient(containerName).GetBlobClient(blobName);
        // Use OpenReadAsync (seekable, length-known) instead of DownloadStreamingAsync
        // (a network stream with no known size). Without a known length, Kestrel serves the
        // response with Transfer-Encoding: chunked and no Content-Length header. Chromium
        // then refuses to buffer past the first chunk — the browser stops fetching after
        // ~2 seconds of video, the audio track never decodes, and the user sees video but
        // hears silence. enableRangeProcessing: true on Results.File also requires a
        // seekable length-known stream to advertise Accept-Ranges: bytes.
        return await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
    }

    public async Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var (containerName, blobName) = SplitPath(path);
        var blobClient = _factory.GetContainerClient(containerName).GetBlobClient(blobName);
        return await blobClient.ExistsAsync(cancellationToken);
    }

    public async Task UploadBlobAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var (containerName, blobName) = SplitPath(path);
        var container = _factory.GetContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var blobClient = container.GetBlobClient(blobName);
        await blobClient.UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public async Task UploadFileAsync(string path, string localFilePath, string contentType, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(localFilePath);
        await UploadBlobAsync(path, stream, contentType, cancellationToken);
    }

    public async IAsyncEnumerable<string> ListBlobsByPrefixAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (containerName, blobPrefix) = SplitPath(prefix);
        var container = _factory.GetContainerClient(containerName);
        await foreach (var blob in container.GetBlobsAsync(prefix: blobPrefix, cancellationToken: cancellationToken))
        {
            yield return $"{containerName}/{blob.Name}";
        }
    }

    public async Task DeleteBlobsByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var (containerName, blobPrefix) = SplitPath(prefix);
        var container = _factory.GetContainerClient(containerName);
        await foreach (var blob in container.GetBlobsAsync(prefix: blobPrefix, cancellationToken: cancellationToken))
        {
            await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
        }
    }

    private static (string Container, string BlobName) SplitPath(string path)
    {
        var slash = path.IndexOf('/');
        if (slash < 0)
            throw new ArgumentException($"Blob path must include a container prefix: {path}", nameof(path));
        return (path[..slash], path[(slash + 1)..]);
    }
}

public static class BlobStorageServiceExtensions
{
    public static IServiceCollection AddBlobStorageService(this IServiceCollection services)
    {
        services.AddSingleton<BlobStorageService>();
        services.AddSingleton<IBlobStorageService>(sp => sp.GetRequiredService<BlobStorageService>());
        return services;
    }
}
