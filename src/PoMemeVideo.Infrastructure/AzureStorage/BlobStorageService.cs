// SOLID: Single Responsibility — all blob I/O isolated here
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using PoMemeVideo.Domain.Interfaces;

namespace PoMemeVideo.Infrastructure.AzureStorage;

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
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
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
