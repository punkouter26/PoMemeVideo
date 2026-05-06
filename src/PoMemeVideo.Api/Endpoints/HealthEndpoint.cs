using Azure.Data.Tables;
using Azure.Storage.Blobs;
using PoMemeVideo.Infrastructure.AzureStorage;

namespace PoMemeVideo.Api.Endpoints;

public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (
            AzureTableClientFactory tableFactory,
            BlobServiceClientFactory blobFactory,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var checks = new Dictionary<string, object>();
            var isHealthy = true;

            // Azure Table Storage
            try
            {
                var tableClient = tableFactory.GetTableClient("HealthCheck");
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

            // Azure AI Vision
            var visionEndpoint = configuration["AzureAiVision:Endpoint"];
            if (string.IsNullOrWhiteSpace(visionEndpoint))
            {
                checks["azureAiVision"] = "Degraded: not configured";
                isHealthy = false;
            }
            else
                checks["azureAiVision"] = "Healthy";

            // Ollama / local models (optional — only unhealthy if it's the active provider)
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var ollamaUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
                var resp = await http.GetAsync($"{ollamaUrl}/api/tags", ct);
                checks["ollamaGemma4"] = resp.IsSuccessStatusCode ? "Healthy" : $"Unavailable: HTTP {(int)resp.StatusCode}";
            }
            catch (Exception)
            {
                // Ollama is optional — not installed locally is expected
                checks["ollamaGemma4"] = $"Unavailable (install Ollama to use local models)";
            }

            var result = new { status = isHealthy ? "Healthy" : "Degraded", checks };
            return isHealthy ? Results.Ok(result) : Results.Json(result, statusCode: 503);
        })
        .WithName("GetHealth")
        .WithTags("Health")
        .Produces<object>(200)
        .Produces<object>(503);

        return app;
    }
}
