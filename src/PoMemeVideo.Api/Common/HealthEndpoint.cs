using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace PoMemeVideo.Api.Common;

public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        // Replaced this app's bespoke liveness payload with the shared one: three apps each
        // answered /health/live in a different shape, so nothing could poll them uniformly.
        app.MapPoLiveness();

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
                var tableClient = tableFactory.GetTableClient(StorageNames.Tables.HealthCheck);
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
                var container = blobClient.GetBlobContainerClient(StorageNames.Containers.Sessions);
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

            // Vision and the director both call the same Azure AI Services (Foundry) resource.
            // `AiFoundry:Endpoint` is canonical; `AzureOpenAI:Endpoint` is the legacy name still
            // accepted as a fallback, and `AzureAiVision:Endpoint` the older Computer Vision one.
            var foundryEndpoint = configuration["AiFoundry:Endpoint"];
            var legacyOpenAiEndpoint = configuration["AzureOpenAI:Endpoint"];
            var computerVisionEndpoint = configuration["AzureAiVision:Endpoint"];

            var chatEndpoint = !string.IsNullOrWhiteSpace(foundryEndpoint) ? foundryEndpoint
                : !string.IsNullOrWhiteSpace(legacyOpenAiEndpoint) ? legacyOpenAiEndpoint
                : null;

            if (chatEndpoint is null && string.IsNullOrWhiteSpace(computerVisionEndpoint))
            {
                checks["aiVision"] = "Degraded: not configured (set AiFoundry:Endpoint)";
                isHealthy = false;
            }
            else
            {
                checks["aiVision"] = chatEndpoint is not null
                    ? "Healthy (AI Foundry chat vision)"
                    : "Healthy (Azure Computer Vision)";
            }

            // The director — one cloud provider, reached through the same endpoint.
            if (chatEndpoint is null)
            {
                checks["aiFoundry"] = "Degraded: not configured";
                isHealthy = false;
            }
            else
            {
                checks["aiFoundry"] = string.IsNullOrWhiteSpace(foundryEndpoint)
                    ? "Healthy (via legacy AzureOpenAI:Endpoint — migrate to AiFoundry:Endpoint)"
                    : "Healthy";
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
        .Produces<object>(503)
        // Monitoring endpoint: must stay reachable without a session, otherwise the
        // deny-by-default FallbackPolicy turns every probe into a 302 to /login.
        // It reports dependency status only — no configuration values are returned.
        .AllowAnonymous();

        return app;
    }
}
