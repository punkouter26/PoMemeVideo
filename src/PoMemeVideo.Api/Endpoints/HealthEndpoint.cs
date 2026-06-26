using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace PoMemeVideo.Api.Endpoints;

public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (
            AzureTableClientFactory tableFactory,
            BlobServiceClientFactory blobFactory,
            IConfiguration configuration,
            IHostEnvironment environment,
            CancellationToken ct) =>
        {
            var checks = new Dictionary<string, object>();
            var isHealthy = true;

            // Azure Table Storage
            try
            {
                var tableClient = tableFactory.GetTableClient("HealthCheck");
                await foreach (var _ in tableClient.QueryAsync<TableEntity>(maxPerPage: 1, cancellationToken: ct))
                {
                    break;
                }
                checks["tableStorage"] = "Healthy";
            }
            catch (Exception ex)
            {
                checks["tableStorage"] = $"Degraded: {ex.Message}";
                isHealthy = false;
            }

            // Azure Blob Storage — use container-level ExistsAsync (requires only
            // container read permission; avoids account-level GetPropertiesAsync).
            try
            {
                var blobClient = blobFactory.GetClient();
                var container = blobClient.GetBlobContainerClient("sessions");
                await container.ExistsAsync(ct);
                checks["blobStorage"] = "Healthy";
            }
            catch (Exception ex)
            {
                checks["blobStorage"] = $"Degraded: {ex.Message}";
                isHealthy = false;
            }

            // Blob CORS check (Development only) — missing CORS rules mean direct browser uploads will fail.
            if (environment.IsDevelopment())
            {
                try
                {
                    var blobClient = blobFactory.GetClient();
                    var props = await blobClient.GetPropertiesAsync(ct);
                    checks["blobCors"] = props.Value.Cors.Count > 0
                        ? "Healthy"
                        : "Degraded: no CORS rules configured — browser uploads will fail. Restart the API after Azurite is running.";
                }
                catch (Exception ex)
                {
                    checks["blobCors"] = $"Degraded: {ex.Message}";
                }
            }

            // Azure AI Vision
            var visionEndpoint = configuration["AzureAiVision:Endpoint"];
            if (string.IsNullOrWhiteSpace(visionEndpoint))
            {
                checks["azureAiVision"] = "Degraded: not configured";
                isHealthy = false;
            }
            else
                checks["azureAiVision"] = "Healthy";

            // Azure OpenAI
            var openAiEndpoint = configuration["AzureOpenAI:Endpoint"];
            if (string.IsNullOrWhiteSpace(openAiEndpoint))
            {
                checks["azureOpenAI"] = "Degraded: not configured";
                isHealthy = false;
            }
            else
            {
                checks["azureOpenAI"] = "Healthy";
            }

            var result = new
            {
                status = isHealthy ? "Healthy" : "Degraded",
                environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName,
                timestampUtc = DateTimeOffset.UtcNow,
                checks
            };
            return isHealthy ? Results.Ok(result) : Results.Json(result, statusCode: 503);
        })
        .WithName("GetHealth")
        .WithTags("Health")
        .Produces<object>(200)
        .Produces<object>(503);

        return app;
    }
}
