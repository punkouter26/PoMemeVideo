using Microsoft.Extensions.Options;
using PoMemeVideo.Api.Configuration;
using PoMemeVideo.Infrastructure;

namespace PoMemeVideo.Api.Features.Config;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", (
            IOptions<FeatureFlags> featureFlags,
            IHostEnvironment environment) =>
        {
            return Results.Ok(new
            {
                useMockAI = featureFlags.Value.UseMockAI,
                isDevelopment = environment.IsDevelopment()
            });
        })
        .WithName("GetConfig")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        // ── AI model selection ───────────────────────────────────────────────
        app.MapGet("/api/config/ai-model", (RuntimeAiSettings settings) =>
        {
            return Results.Ok(new
            {
                provider = settings.Provider,
                ollamaModel = settings.OllamaModel,
                availableLocalModels = RuntimeAiSettings.LocalModels,
            });
        })
        .WithName("GetAiModel")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        app.MapPut("/api/config/ai-model", (AiModelRequest req, RuntimeAiSettings settings) =>
        {
            if (req.Provider != "AzureOpenAI" && req.Provider != "Ollama")
                return Results.BadRequest("provider must be 'AzureOpenAI' or 'Ollama'.");

            if (req.Provider == "Ollama")
            {
                if (string.IsNullOrWhiteSpace(req.OllamaModel))
                    return Results.BadRequest("ollamaModel is required when provider is 'Ollama'.");
                if (!RuntimeAiSettings.LocalModels.Contains(req.OllamaModel))
                    return Results.BadRequest($"Unknown ollamaModel. Valid: {string.Join(", ", RuntimeAiSettings.LocalModels)}");
                settings.OllamaModel = req.OllamaModel;
            }

            settings.Provider = req.Provider;

            return Results.Ok(new
            {
                provider = settings.Provider,
                ollamaModel = settings.OllamaModel,
            });
        })
        .WithName("SetAiModel")
        .WithTags("Config")
        .Produces<object>(200)
        .ProducesProblem(400)
        .AllowAnonymous();

        return app;
    }

    private sealed record AiModelRequest(string Provider, string? OllamaModel);
}

