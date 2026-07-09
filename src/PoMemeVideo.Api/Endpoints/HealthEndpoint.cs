using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace PoMemeVideo.Api.Endpoints;

public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        // Liveness: dependency-free uptime ping for the platform warm-up probe and the
        // deploy health gate. Never returns 503 for a degraded dependency.
        app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }))
            .WithName("GetHealthLive")
            .WithTags("Health")
            .Produces<object>(200)
            .AllowAnonymous();

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

            // AI vision is provided by AzureOpenAiVisionService (gpt-5.4-nano chat
            // completions with image parts). It reads AzureOpenAI:Endpoint + Key, so we
            // report Healthy when that endpoint is configured. The legacy AzureAiVision
            // (Computer Vision) key is still accepted as a fallback.
            var visionEndpoint = configuration["AzureAiVision:Endpoint"];
            var openAiEndpoint = configuration["AzureOpenAI:Endpoint"];
            if (string.IsNullOrWhiteSpace(visionEndpoint) && string.IsNullOrWhiteSpace(openAiEndpoint))
            {
                checks["azureAiVision"] = "Degraded: not configured (set AzureOpenAI:Endpoint or AzureAiVision:Endpoint)";
                isHealthy = false;
            }
            else
            {
                checks["azureAiVision"] = string.IsNullOrWhiteSpace(visionEndpoint)
                    ? "Healthy (Azure OpenAI gpt-5.4-nano vision)"
                    : "Healthy (Azure Computer Vision)";
            }

            // Azure OpenAI director / chat completions
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
