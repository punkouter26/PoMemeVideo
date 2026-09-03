using Microsoft.AspNetCore.Mvc;
using PoMemeVideo.Api;
using System.Text.Json;

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
            [FromServices] FoundryDeploymentLister foundry,
            IConfiguration configuration,
            IWebHostEnvironment env) =>
        {
            var localModelIds = GetAvailableLocalModelIds(env);
            var selectedBrowserLLMModel = RuntimeAiSettings.LocalModelDisplayNames.ContainsKey(settings.BrowserLLMModel)
                ? settings.BrowserLLMModel
                : RuntimeAiSettings.LocalModelDisplayNames.Keys.FirstOrDefault();

            // Enumerate AI Foundry / Azure OpenAI deployments from ARM.
            // On any failure (no AAD session, network, missing subscription) we fall back
            // to a curated list so the dropdown still has selectable values.
            var foundryDeployments = await foundry.ListAsync(default);
            var foundryDeploymentNames = foundryDeployments
                .Select(d => d.Name)
                .ToArray();

            // Curated fallback list — common GPT-5 / o-series names. Used when ARM call
            // returned empty (e.g. no AAD credential available). Operators can extend via
            // AiFoundry:KnownDeployments (comma-separated) in appsettings.
            var curated = configuration
                .GetSection("AiFoundry:KnownDeployments")
                .Get<string[]>()
                ?? Array.Empty<string>();

            if (foundryDeploymentNames.Length == 0 && curated.Length > 0)
                foundryDeploymentNames = curated;

            // If the cached/active deployment isn't in the live list (e.g. it was just
            // deleted), keep it visible so the user can still see what's selected.
            var selectedFoundry = settings.AiFoundryDeployment;
            var allFoundryNames = foundryDeploymentNames.Contains(selectedFoundry, StringComparer.OrdinalIgnoreCase)
                ? foundryDeploymentNames
                : new[] { selectedFoundry }.Concat(foundryDeploymentNames).Distinct().ToArray();

            return Results.Ok(new
            {
                provider = settings.Provider,
                browserLLMModel = selectedBrowserLLMModel,
                localModels = RuntimeAiSettings.LocalModelDisplayNames.Select(model => new
                {
                    id = model.Key,
                    label = model.Value,
                    available = localModelIds.Contains(model.Key, StringComparer.OrdinalIgnoreCase),
                }),
                aiFoundryDeployment = selectedFoundry,
                aiFoundryDeployments = allFoundryNames,
                aiFoundryDeploymentDetails = foundryDeployments.Select(d => new
                {
                    name = d.Name,
                    model = d.ModelName,
                    version = d.ModelVersion,
                    provisioningState = d.ProvisioningState,
                    capacity = d.Capacity,
                    skuName = d.SkuName,
                }),
                isDevelopment = env.IsDevelopment(),
            });
        })
        .WithName("GetAiModel")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        app.MapPut("/api/config/ai-model", (
            AiModelRequest req,
            [FromServices] RuntimeAiSettings settings,
            IWebHostEnvironment env) =>
        {
            if (!RuntimeAiSettings.ValidProviders.Contains(req.Provider))
                return Results.BadRequest($"provider must be one of: {string.Join(", ", RuntimeAiSettings.ValidProviders)}.");

            var localModelIds = GetAvailableLocalModelIds(env);

            switch (req.Provider)
            {
                case "BrowserLLM":
                    if (localModelIds.Length == 0)
                        return Results.BadRequest("No local BrowserLLM models are installed. Run 'python scripts/download-models.py' first.");
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

                default: // AzureOpenAI — allow pre-selecting a BrowserLLM model while switching
                    if (!string.IsNullOrWhiteSpace(req.BrowserLLMModel)
                        && localModelIds.Contains(req.BrowserLLMModel, StringComparer.OrdinalIgnoreCase))
                        settings.BrowserLLMModel = req.BrowserLLMModel;
                    break;
            }

            settings.Provider = req.Provider;
            PersistSettings(settings);

            return Results.Ok(new
            {
                provider = settings.Provider,
                browserLLMModel = settings.BrowserLLMModel,
                aiFoundryDeployment = settings.AiFoundryDeployment,
            });
        })
        .WithName("SetAiModel")
        .WithTags("Config")
        .Produces<object>(200)
        .ProducesProblem(400)
        .AllowAnonymous();

        return app;
    }

    private sealed record AiModelRequest(
        string Provider,
        string? BrowserLLMModel,
        string? AiFoundryDeployment);

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

    /// <summary>
    /// Resolves the persisted AI-settings file path. Off by default — the file lives under
    /// <c>%LOCALAPPDATA%/PoMemeVideo</c> when the env var <c>PoMemeVideo__PersistAiSettings=true</c>
    /// is set, otherwise persistence is skipped entirely. %TEMP% was rejected because reboots
    /// silently drop the file (which masked the "provider flipped on me" bug we hit in dev).
    /// </summary>
    private static string? GetSettingsFilePath()
    {
        var enabled = Environment.GetEnvironmentVariable("PoMemeVideo__PersistAiSettings");
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            return null;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PoMemeVideo");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ai-settings.json");
    }

    private static void PersistSettings(RuntimeAiSettings settings)
    {
        var path = GetSettingsFilePath();
        if (path is null) return;

        try
        {
            var data = new
            {
                provider = settings.Provider,
                browserLLMModel = settings.BrowserLLMModel,
                aiFoundryDeployment = settings.AiFoundryDeployment,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(data));
        }
        catch
        {
            // Non-fatal — settings are still active in-memory for this run
        }
    }

    public static void RestoreSettings(RuntimeAiSettings settings)
    {
        var path = GetSettingsFilePath();
        if (path is null || !File.Exists(path))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.TryGetProperty("provider", out var p) && p.GetString() is { } provider
                && RuntimeAiSettings.ValidProviders.Contains(provider))
                settings.Provider = provider;

            if (root.TryGetProperty("browserLLMModel", out var b) && b.GetString() is { } browserModel)
                settings.BrowserLLMModel = browserModel;

            if (root.TryGetProperty("aiFoundryDeployment", out var f) && f.GetString() is { } foundry)
                settings.AiFoundryDeployment = foundry;
        }
        catch
        {
            // Non-fatal — defaults remain active
        }
    }
}
