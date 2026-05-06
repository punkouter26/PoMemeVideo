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
            var useMockAi = configuration.GetValue<bool>("FeatureFlags:UseMockAI", true);

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

            // Azure Blob Storage
            try
            {
                var blobClient = blobFactory.GetClient();
                await blobClient.GetPropertiesAsync(cancellationToken: ct);
                checks["blobStorage"] = "Healthy";
            }
            catch (Exception ex)
            {
                checks["blobStorage"] = $"Degraded: {ex.Message}";
                isHealthy = false;
            }

            // Azure AI Vision
            var visionEndpoint = configuration["AzureAiVision:Endpoint"];
            if (useMockAi)
                checks["azureAiVision"] = "Skipped (mock mode)";
            else if (string.IsNullOrWhiteSpace(visionEndpoint))
            {
                checks["azureAiVision"] = "Degraded: not configured";
                isHealthy = false;
            }
            else
                checks["azureAiVision"] = "Healthy";

            // Ollama / Gemma 4
            if (useMockAi)
            {
                checks["ollamaGemma4"] = "Skipped (mock mode)";
            }
            else
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    var ollamaUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
                    var resp = await http.GetAsync($"{ollamaUrl}/api/tags", ct);
                    checks["ollamaGemma4"] = resp.IsSuccessStatusCode ? "Healthy" : $"Degraded: HTTP {(int)resp.StatusCode}";
                    if (!resp.IsSuccessStatusCode) isHealthy = false;
                }
                catch (Exception ex)
                {
                    checks["ollamaGemma4"] = $"Degraded: {ex.Message}";
                    isHealthy = false;
                }
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
