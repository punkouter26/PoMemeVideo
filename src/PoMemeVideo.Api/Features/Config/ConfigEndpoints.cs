using Microsoft.AspNetCore.Mvc;
using PoMemeVideo.Infrastructure;

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
        app.MapGet("/api/config/ai-model", ([FromServices] RuntimeAiSettings settings, IWebHostEnvironment env) =>
        {
            var localModelIds = GetAvailableLocalModelIds(env);
            var selectedBrowserLLMModel = localModelIds.Contains(settings.BrowserLLMModel, StringComparer.OrdinalIgnoreCase)
                ? settings.BrowserLLMModel
                : localModelIds.FirstOrDefault();

            return Results.Ok(new
            {
                provider = settings.Provider,
                browserLLMModel = selectedBrowserLLMModel,
                localModels = localModelIds.Select(id => new
                {
                    id,
                    label = RuntimeAiSettings.LocalModelDisplayNames.TryGetValue(id, out var label)
                        ? label
                        : id,
                }),
                isDevelopment = env.IsDevelopment(),
            });
        })
        .WithName("GetAiModel")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        app.MapPut("/api/config/ai-model", (AiModelRequest req, [FromServices] RuntimeAiSettings settings, IWebHostEnvironment env) =>
        {
            if (req.Provider != "AzureOpenAI" && req.Provider != "BrowserLLM")
                return Results.BadRequest("provider must be 'AzureOpenAI' or 'BrowserLLM'.");

            var localModelIds = GetAvailableLocalModelIds(env);

            if (req.Provider == "BrowserLLM")
            {
                if (localModelIds.Length == 0)
                    return Results.BadRequest("No local BrowserLLM models are installed. Run 'python tools/download-models.py' first.");

                if (string.IsNullOrWhiteSpace(req.BrowserLLMModel))
                    return Results.BadRequest("browserLLMModel is required when provider is 'BrowserLLM'.");

                if (!localModelIds.Contains(req.BrowserLLMModel, StringComparer.OrdinalIgnoreCase))
                    return Results.BadRequest($"Unknown browserLLMModel '{req.BrowserLLMModel}'.");

                settings.BrowserLLMModel = req.BrowserLLMModel;
            }
            else if (!string.IsNullOrWhiteSpace(req.BrowserLLMModel)
                     && localModelIds.Contains(req.BrowserLLMModel, StringComparer.OrdinalIgnoreCase))
            {
                // Allow pre-selecting a local model while AzureOpenAI is active.
                settings.BrowserLLMModel = req.BrowserLLMModel;
            }

            settings.Provider = req.Provider;

            return Results.Ok(new
            {
                provider = settings.Provider,
                browserLLMModel = settings.BrowserLLMModel,
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

        var ids = Directory
            .GetDirectories(modelsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ids;
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

    private sealed record AiModelRequest(string Provider, string? BrowserLLMModel);
}

