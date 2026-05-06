using Microsoft.AspNetCore.Mvc;
using PoMemeVideo.Infrastructure;

namespace PoMemeVideo.Api.Features.Config;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", (
            IHostEnvironment environment) =>
        {
            return Results.Ok(new
            {
                isDevelopment = environment.IsDevelopment()
            });
        })
        .WithName("GetConfig")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        // ── AI model selection ───────────────────────────────────────────────
        app.MapGet("/api/config/ai-model", async ([FromServices] RuntimeAiSettings settings, IConfiguration config, IHostEnvironment env, CancellationToken ct) =>
        {
            // Probe Ollama availability (quick, non-blocking)
            var ollamaAvailable = false;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var ollamaUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
                var resp = await http.GetAsync($"{ollamaUrl}/api/tags", ct);
                ollamaAvailable = resp.IsSuccessStatusCode;
            }
            catch { /* not installed */ }

            return Results.Ok(new
            {
                provider = settings.Provider,
                ollamaModel = settings.OllamaModel,
                availableLocalModels = RuntimeAiSettings.LocalModels,
                browserLLMModel = RuntimeAiSettings.BrowserLLMModel,
                ollamaAvailable,
                isDevelopment = env.IsDevelopment(),
            });
        })
        .WithName("GetAiModel")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        app.MapPut("/api/config/ai-model", (AiModelRequest req, [FromServices] RuntimeAiSettings settings) =>
        {
            if (req.Provider != "AzureOpenAI" && req.Provider != "Ollama" && req.Provider != "BrowserLLM")
                return Results.BadRequest("provider must be 'AzureOpenAI', 'Ollama', or 'BrowserLLM'.");

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

