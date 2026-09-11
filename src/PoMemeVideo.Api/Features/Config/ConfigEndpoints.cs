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
            IConfiguration configuration) =>
        {
            return Results.Ok(new
            {
                isDevelopment = environment.IsDevelopment(),
                useMockAI = configuration.GetValue<bool>("UseMockAI"),
            });
        })
        .WithName("GetConfig")
        .WithTags("Config")
        .Produces<object>(200)
        .AllowAnonymous();

        // ── AI deployment selection ──────────────────────────────────────────
        //
        // The deployment list comes from AiFoundry:KnownDeployments in configuration. It was
        // previously enumerated live from ARM by FoundryDeploymentLister, which needed an AAD
        // session the app does not have in production, fell back to this same curated list on
        // every failure, and existed to populate a dropdown that also offered browser-side
        // ONNX models. Reading the configured list directly is what actually happened in
        // practice, minus 300 lines and an ARM round-trip on every page load.
        app.MapGet("/api/config/ai-model", (
            [FromServices] RuntimeAiSettings settings,
            IConfiguration configuration,
            IWebHostEnvironment env) =>
        {
            var selected = settings.AiFoundryDeployment;
            var deployments = GetKnownDeployments(configuration, selected);

            return Results.Ok(new
            {
                aiFoundryDeployment = selected,
                aiFoundryDeployments = deployments,
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
            IConfiguration configuration) =>
        {
            if (string.IsNullOrWhiteSpace(req.AiFoundryDeployment))
                return Results.BadRequest("aiFoundryDeployment is required.");

            // Only names the operator has configured are accepted. Without this the field is a
            // free-text value that reaches the Foundry endpoint as a deployment id.
            var known = GetKnownDeployments(configuration, settings.AiFoundryDeployment);
            if (!known.Contains(req.AiFoundryDeployment, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest($"Unknown deployment '{req.AiFoundryDeployment}'.");

            settings.AiFoundryDeployment = req.AiFoundryDeployment;
            PersistSettings(settings);

            return Results.Ok(new { aiFoundryDeployment = settings.AiFoundryDeployment });
        })
        .WithName("SetAiModel")
        .WithTags("Config")
        .Produces<object>(200)
        .ProducesProblem(400)
        .AllowAnonymous();

        return app;
    }

    private sealed record AiModelRequest(string? AiFoundryDeployment);

    /// <summary>
    /// Configured deployment names, with the active one always present so a selection made
    /// before a configuration change stays visible in the dropdown.
    /// </summary>
    private static string[] GetKnownDeployments(IConfiguration configuration, string selected)
    {
        var curated = configuration
            .GetSection("AiFoundry:KnownDeployments")
            .Get<string[]>()
            ?? [];

        return curated.Contains(selected, StringComparer.OrdinalIgnoreCase)
            ? curated
            : [selected, .. curated];
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
            var data = new { aiFoundryDeployment = settings.AiFoundryDeployment };
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
            if (doc.RootElement.TryGetProperty("aiFoundryDeployment", out var f)
                && f.GetString() is { } foundry)
                settings.AiFoundryDeployment = foundry;
        }
        catch
        {
            // Non-fatal — defaults remain active
        }
    }
}
