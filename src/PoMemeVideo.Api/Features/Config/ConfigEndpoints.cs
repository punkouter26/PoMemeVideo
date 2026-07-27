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
            [FromServices] ILocalModelCatalog ollama,
            [FromServices] FoundryDeploymentLister foundry,
            IConfiguration configuration,
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
                localModels = localModelIds.Select(id => new
                {
                    id,
                    label = RuntimeAiSettings.LocalModelDisplayNames.TryGetValue(id, out var label) ? label : id,
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
            [FromServices] ILocalModelCatalog ollama,
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
            PersistSettings(settings);

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

        // ── Model download trigger (dev only) ─────────────────────────────────
        app.MapPost("/api/config/ai-model/download", async (
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            // Gate to Development only — prod uses cloud AI exclusively.
            if (!env.IsDevelopment())
                return Results.BadRequest(new { error = "Model download is only available in Development." });

            var scriptsDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "SCRIPTS"));
            if (!Directory.Exists(scriptsDir))
                return Results.BadRequest(new { error = "SCRIPTS directory not found.", path = scriptsDir });

            var downloadScript = Path.Combine(scriptsDir, "download-models.py");
            if (!File.Exists(downloadScript))
                return Results.BadRequest(new { error = "download-models.py not found.", path = downloadScript });

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{downloadScript}\"",
                    WorkingDirectory = scriptsDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                var process = System.Diagnostics.Process.Start(psi);
                if (process is null)
                    return Results.Problem("Failed to start download process.");

                // Read output asynchronously with a reasonable timeout.
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                await process.WaitForExitAsync(linkedCts.Token);

                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();

                return Results.Ok(new
                {
                    exitCode = process.ExitCode,
                    success = process.ExitCode == 0,
                    output = stdout,
                    error = stderr.Length > 0 ? stderr[..System.Math.Min(stderr.Length, 2000)] : null,
                });
            }
            catch (OperationCanceledException)
            {
                return Results.Problem("Model download timed out after 10 minutes.");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Download failed: {ex.Message}");
            }
        })
        .WithName("DownloadModels")
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

    private static readonly string SettingsFilePath =
        Path.Combine(Path.GetTempPath(), "pomemevideo-ai-settings.json");

    private static void PersistSettings(RuntimeAiSettings settings)
    {
        try
        {
            var data = new
            {
                provider = settings.Provider,
                browserLLMModel = settings.BrowserLLMModel,
                aiFoundryDeployment = settings.AiFoundryDeployment,
                ollamaModel = settings.OllamaModel,
            };
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(data));
        }
        catch
        {
            // Non-fatal — settings are still active in-memory for this run
        }
    }

    public static void RestoreSettings(RuntimeAiSettings settings)
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return;

            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFilePath));
            var root = doc.RootElement;

            if (root.TryGetProperty("provider", out var p) && p.GetString() is { } provider
                && RuntimeAiSettings.ValidProviders.Contains(provider))
                settings.Provider = provider;

            if (root.TryGetProperty("browserLLMModel", out var b) && b.GetString() is { } browserModel)
                settings.BrowserLLMModel = browserModel;

            if (root.TryGetProperty("aiFoundryDeployment", out var f) && f.GetString() is { } foundry)
                settings.AiFoundryDeployment = foundry;

            if (root.TryGetProperty("ollamaModel", out var o) && o.GetString() is { } ollama)
                settings.OllamaModel = ollama;
        }
        catch
        {
            // Non-fatal — defaults remain active
        }
    }
}

