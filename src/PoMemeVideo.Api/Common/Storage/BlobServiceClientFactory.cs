// SOLID: Single Responsibility — all Blob Storage client creation isolated here
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PoMemeVideo.Api.Common;

public class BlobServiceClientFactory
{
    private readonly BlobServiceClient _client;
    private readonly ILogger<BlobServiceClientFactory> _logger;

    public BlobServiceClientFactory(IConfiguration configuration, ILogger<BlobServiceClientFactory> logger)
    {
        _logger = logger;
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
    /// Configures CORS on the blob service to allow browser direct-upload from the given origin(s).
    /// Works against Azurite (local) and real Azure Storage (prod, via the account-key connection
    /// string). Retries up to 3 times so startup ordering (API before Azurite) is tolerated.
    /// </summary>
    public async Task EnsureUploadCorsAsync(string allowedOrigin, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
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
                _logger.LogInformation("Blob CORS configured for origins: {Origins}", allowedOrigin);
                return;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogWarning(ex,
                        "Blob CORS configuration failed after {Attempts} attempt(s). " +
                        "Direct browser uploads will fail until the API is restarted with storage already running. " +
                        "Ensure Azurite/Azure Blob Storage is reachable before starting the API.",
                        maxAttempts);
                    return;
                }
                _logger.LogDebug(ex, "Blob CORS attempt {Attempt}/{Max} failed, retrying in 2s…", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
}

public static class BlobServiceClientFactoryExtensions
{
    public static IServiceCollection AddBlobServiceClientFactory(this IServiceCollection services)
        => services.AddSingleton<BlobServiceClientFactory>();
}
