using Microsoft.AspNetCore.Mvc;
using PoMemeVideo.Infrastructure;
using PoMemeVideo.Infrastructure.Ollama;

namespace PoMemeVideo.Api.Features.Config;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", (
            IHostEnvironment environment,
            RuntimeAiSettings settings,
            IConfiguration configuration) =>
        {
            return Results.Ok(new
            {
                isDevelopment = environment.IsDevelopment(),
                provider = settings.Provider,
                useMockAI = configuration.GetValue<bool>("UseMockAI")
            });
        })
        .WithName("GetConfig")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        // ── AI model selection ───────────────────────────────────────────────
        app.MapGet("/api/config/ai-model", async (
            [FromServices] RuntimeAiSettings settings,
            [FromServices] OllamaDirectorService ollama,
            IWebHostEnvironment env) =>
        {
            var localModelIds = GetAvailableLocalModelIds(env);
            var selectedBrowserLLMModel = localModelIds.Contains(settings.BrowserLLMModel, StringComparer.OrdinalIgnoreCase)
                ? settings.BrowserLLMModel
                : localModelIds.FirstOrDefault();

            // Probe Ollama only in Development to avoid blocking prod startup.
            string[]? ollamaModels = null;
            if (env.IsDevelopment())
                ollamaModels = await ollama.GetInstalledModelsAsync();

            return Results.Ok(new
            {
                provider = settings.Provider,
                browserLLMModel = selectedBrowserLLMModel,
                localModels = localModelIds.Select(id => new
                {
                    id,
                    label = RuntimeAiSettings.LocalModelDisplayNames.TryGetValue(id, out var label) ? label : id,
                }),
                aiFoundryDeployment = settings.AiFoundryDeployment,
                aiFoundryDeployments = new[] { "gpt-4o", "gpt-4.1-mini", "gpt-4.1-nano", "Phi-4-mini-instruct" },
                ollamaAvailable = ollamaModels is not null,
                ollamaModel = settings.OllamaModel,
                ollamaModels = ollamaModels ?? [],
                isDevelopment = env.IsDevelopment(),
            });
        })
        .WithName("GetAiModel")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        app.MapPut("/api/config/ai-model", async (
            AiModelRequest req,
            [FromServices] RuntimeAiSettings settings,
            [FromServices] OllamaDirectorService ollama,
            IWebHostEnvironment env) =>
        {
            if (!RuntimeAiSettings.ValidProviders.Contains(req.Provider))
                return Results.BadRequest($"provider must be one of: {string.Join(", ", RuntimeAiSettings.ValidProviders)}.");

            // Ollama is only allowed in Development.
            if (req.Provider == "Ollama" && !env.IsDevelopment())
                return Results.BadRequest("Ollama is only available in Development environments.");

            var localModelIds = GetAvailableLocalModelIds(env);

            switch (req.Provider)
            {
                case "BrowserLLM":
                    if (localModelIds.Length == 0)
                        return Results.BadRequest("No local BrowserLLM models are installed. Run 'python SCRIPTS/download-models.py' first.");
                    if (string.IsNullOrWhiteSpace(req.BrowserLLMModel))
                        return Results.BadRequest("browserLLMModel is required when provider is 'BrowserLLM'.");
                    if (!localModelIds.Contains(req.BrowserLLMModel, StringComparer.OrdinalIgnoreCase))
                        return Results.BadRequest($"Unknown browserLLMModel '{req.BrowserLLMModel}'.");
                    settings.BrowserLLMModel = req.BrowserLLMModel;
                    break;

                case "AiFoundry":
                    if (!string.IsNullOrWhiteSpace(req.AiFoundryDeployment))
                        settings.AiFoundryDeployment = req.AiFoundryDeployment;
                    break;

                case "Ollama":
                    var installedModels = await ollama.GetInstalledModelsAsync();
                    if (installedModels is null)
                        return Results.BadRequest("Ollama is not running. Start Ollama and try again.");
                    if (!string.IsNullOrWhiteSpace(req.OllamaModel))
                    {
                        if (installedModels.Length > 0 && !installedModels.Contains(req.OllamaModel, StringComparer.OrdinalIgnoreCase))
                            return Results.BadRequest($"Ollama model '{req.OllamaModel}' is not installed. Run: ollama pull {req.OllamaModel}");
                        settings.OllamaModel = req.OllamaModel;
                    }
                    break;

                default: // AzureOpenAI — allow pre-selecting a BrowserLLM model while switching
                    if (!string.IsNullOrWhiteSpace(req.BrowserLLMModel)
                        && localModelIds.Contains(req.BrowserLLMModel, StringComparer.OrdinalIgnoreCase))
                        settings.BrowserLLMModel = req.BrowserLLMModel;
                    break;
            }

            settings.Provider = req.Provider;

            return Results.Ok(new
            {
                provider = settings.Provider,
                browserLLMModel = settings.BrowserLLMModel,
                aiFoundryDeployment = settings.AiFoundryDeployment,
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

    private static string[] GetAvailableLocalModelIds(IWebHostEnvironment env)
    {
        var modelsRoot = ResolveModelsRoot(env.ContentRootPath);
        if (modelsRoot is null)
            return [];

        return Directory
            .GetDirectories(modelsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveModelsRoot(string contentRoot)
    {
        var candidates = new[]
        {
            Path.Combine(contentRoot, "MODEL"),
            Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "MODEL")),
            Path.Combine(Directory.GetCurrentDirectory(), "MODEL"),
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private sealed record AiModelRequest(
        string Provider,
        string? BrowserLLMModel,
        string? AiFoundryDeployment,
        string? OllamaModel);
}

