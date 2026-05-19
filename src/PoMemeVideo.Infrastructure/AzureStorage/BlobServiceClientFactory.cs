// SOLID: Single Responsibility — all Blob Storage client creation isolated here
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PoMemeVideo.Infrastructure.AzureStorage;

public class BlobServiceClientFactory
{
    private readonly BlobServiceClient _client;

    public BlobServiceClientFactory(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AzureBlobStorage");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _client = new BlobServiceClient(connectionString);
        }
        else if (string.Equals(configuration["Azure:BlobStorage:UseDevelopmentStorage"], "true", StringComparison.OrdinalIgnoreCase))
        {
            _client = new BlobServiceClient("UseDevelopmentStorage=true");
        }
        else
        {
            var endpoint = configuration["Azure:BlobStorage:Endpoint"]
                ?? throw new InvalidOperationException("Azure Blob Storage endpoint not configured.");
            _client = new BlobServiceClient(new Uri(endpoint), new DefaultAzureCredential());
        }
    }

    public BlobServiceClient GetClient() => _client;

    public BlobContainerClient GetContainerClient(string containerName)
    {
        var container = _client.GetBlobContainerClient(containerName);
        container.CreateIfNotExists();
        return container;
    }

    /// <summary>
    /// Generates a write-scoped SAS URI for the given blob path.
    /// Works with both connection-string-based (Azurite) and managed-identity-based clients.
    /// </summary>
    public virtual async Task<Uri> GenerateUploadSasUriAsync(
        string blobPath,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var slash = blobPath.IndexOf('/');
        if (slash < 0)
            throw new ArgumentException($"Blob path must include a container prefix: {blobPath}", nameof(blobPath));

        var containerName = blobPath[..slash];
        var blobName = blobPath[(slash + 1)..];

        // Ensure the container exists before issuing a SAS URI against it
        var containerClient = _client.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        // Use raw container client (no CreateIfNotExists) — SAS generation is a local operation
        var blobClient = containerClient.GetBlobClient(blobName);

        // Connection-string-based client (Azurite or shared-key Azure): direct SAS generation
        if (blobClient.CanGenerateSasUri)
        {
            return blobClient.GenerateSasUri(
                BlobSasPermissions.Write | BlobSasPermissions.Create,
                expiresAt);
        }

        // Managed-identity-based client: user delegation SAS
        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
        var userDelegationKey = await _client.GetUserDelegationKeyAsync(startsOn, expiresAt, cancellationToken);

        var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Write | BlobSasPermissions.Create, expiresAt)
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            StartsOn = startsOn,
        };

        var queryParams = sasBuilder.ToSasQueryParameters(userDelegationKey.Value, _client.AccountName);
        return new Uri($"{blobClient.Uri}?{queryParams}");
    }

    /// <summary>
    /// Configures CORS on the blob service to allow browser direct-upload from the given origin.
    /// Safe to call on Azurite; no-ops gracefully on managed-identity clients without CORS support.
    /// </summary>
    public async Task EnsureDevCorsAsync(string allowedOrigin, CancellationToken cancellationToken = default)
    {
        try
        {
            var props = await _client.GetPropertiesAsync(cancellationToken);
            props.Value.Cors.Clear();
            props.Value.Cors.Add(new Azure.Storage.Blobs.Models.BlobCorsRule
            {
                AllowedOrigins = allowedOrigin,
                AllowedMethods = "PUT,GET,HEAD,DELETE,OPTIONS",
                AllowedHeaders = "*",
                ExposedHeaders = "ETag,x-ms-request-id,x-ms-version",
                MaxAgeInSeconds = 3600
            });
            await _client.SetPropertiesAsync(props.Value, cancellationToken);
        }
        catch
        {
            // Non-fatal in dev — log and continue
        }
    }
}

public static class BlobServiceClientFactoryExtensions
{
    public static IServiceCollection AddBlobServiceClientFactory(this IServiceCollection services)
        => services.AddSingleton<BlobServiceClientFactory>();
}
