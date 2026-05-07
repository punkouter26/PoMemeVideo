using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoMemeVideo.Api.Endpoints;
using PoMemeVideo.Api.Features.Auth;
using PoMemeVideo.Api.Features.Config;
using PoMemeVideo.Api.Features.Ingestion;
using PoMemeVideo.Api.Features.MemeLibrary;
using PoMemeVideo.Api.Features.Admin;
using PoMemeVideo.Api.Features.Output;
using PoMemeVideo.Api.Features.Processing;
using PoMemeVideo.Api.Hubs;
using PoMemeVideo.Application.Ingestion;
using PoMemeVideo.Application.MemeLibrary;
using PoMemeVideo.Application.Processing;
using PoMemeVideo.Application.Rendering;
using PoMemeVideo.Infrastructure.FFmpeg;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure;
using PoMemeVideo.Infrastructure.AzureOpenAi;
using PoMemeVideo.Infrastructure.AzureStorage;
using PoMemeVideo.Infrastructure.BrowserLlm;
using PoMemeVideo.Infrastructure.Mock;
using PoMemeVideo.Infrastructure.Ollama;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// ── CLI verb: dotnet run -- seed-sounds [--seeds-dir <path>] ─────────────────
// Short-circuits before web host construction so no Azure auth/storage is needed.
if (args.Length > 0 && args[0] == "seed-sounds")
{
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    return await SeedSoundsCommand.RunAsync(args[1..], config);
}

// Bootstrap logger for startup errors
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Azure Key Vault configuration ───────────────────────────────────────
    var kvUri = builder.Configuration["KeyVault:Uri"]
                ?? "https://kv-poshared.vault.azure.net/";
    // In dev, AzureCliCredential skips ~10 token sources and starts in ~1s.
    // DefaultAzureCredential is retained for all other environments.
    TokenCredential credential = builder.Environment.IsDevelopment()
        ? new AzureCliCredential()
        : new DefaultAzureCredential();
    builder.Configuration.AddAzureKeyVault(
        new SecretClient(new Uri(kvUri), credential),
        new PrefixKeyVaultSecretManager("PoMemeVideo"));

    // Re-apply dev-only overrides after Key Vault by adding a new InMemoryCollection
    // source that comes after KV in the provider chain (later = higher priority).
    if (builder.Environment.IsDevelopment())
    {
        var devOverrides = new ConfigurationBuilder()
            .SetBasePath(builder.Environment.ContentRootPath)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();
        var overrideDict = devOverrides.AsEnumerable()
            .Where(kv => kv.Value is not null)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (overrideDict.Count > 0)
            builder.Configuration.AddInMemoryCollection(overrideDict);
    }

    // ── Serilog (T014) ──────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, config) =>
    {
        var loggerConfig = config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "PoMemeVideo")
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/pomemevideo-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30);

        var appInsightsConnStr = context.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(appInsightsConnStr))
        {
            loggerConfig.WriteTo.ApplicationInsights(
                appInsightsConnStr,
                TelemetryConverter.Traces);
        }
    });

    // ── OpenTelemetry (T015) ────────────────────────────────────────────────
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("PoMemeVideo"))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("PoMemeVideo.*");

            var otlpEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            }
        });

    // ── Azure Storage (T016, T017, T017b) ──────────────────────────────────
    builder.Services.AddAzureTableClientFactory();
    builder.Services.AddBlobServiceClientFactory();
    builder.Services.AddBlobStorageService();
    // ── Auth (T076) ────────────────────────────────────────────────────
    builder.Services.AddUserIdentityTableRepository();
    // ── Ingestion (T025, T026) ───────────────────────────────────────────
    builder.Services.AddVideoSessionTableRepository();
    builder.Services.AddScoped<IngestVideoCommand>();

    // ── Phase 4: Processing pipeline (T038–T048) ─────────────────────────
    builder.Services.AddSoundAssetTableRepository();
    builder.Services.AddDirectorScriptTableRepository();

    // AI services — RuntimeAiSettings required by /api/config/ai-model endpoints
    builder.Services.AddSingleton<RuntimeAiSettings>();
    // Runtime-switchable AI settings (default: AzureOpenAI)
    builder.Services.AddHttpClient();  // registers IHttpClientFactory
    builder.Services.AddSingleton<IAiVisionService, AzureOpenAiVisionService>();
    builder.Services.AddSingleton<AzureOpenAiDirectorService>();
    builder.Services.AddSingleton<OllamaDirectorService>();
    builder.Services.AddSingleton<BrowserLLMDirectorService>();
    builder.Services.AddSingleton<MockDirectorService>();
    builder.Services.AddKeyedSingleton<IDirectorService, MockDirectorService>("mock");
    builder.Services.AddSingleton<IDirectorService, SwitchingDirectorService>();

    builder.Services.AddScoped<SemanticMatchingService>();
    builder.Services.AddScoped<RunEngineCommand>();
    builder.Services.AddScoped<RenderVideoCommand>();
    builder.Services.AddSingleton<FFmpegRenderService>();
    builder.Services.AddSingleton<IVideoRenderService>(sp => sp.GetRequiredService<FFmpegRenderService>());

    // ── Authentication (T019, T078) ──────────────────────────────────────────
    // Microsoft.Identity.Web registers both OpenIdConnect and Cookie schemes.
    // Cookie scheme is used for both ANON sign-in and OIDC session persistence.
    // Gracefully degrades when AzureAd:ClientId is absent (ANON-only dev mode).
    var azureAdSection = builder.Configuration.GetSection("AzureAd");
    var hasAzureAd = !string.IsNullOrWhiteSpace(azureAdSection["ClientId"]);

    if (hasAzureAd)
    {
        builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(azureAdSection);
    }
    else
    {
        // Dev fallback: cookie-only auth (no Azure AD configured)
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/auth/logout";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });
    }
    builder.Services.AddAuthorization();

    // ── SignalR (T020) ──────────────────────────────────────────────────────
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<IEngineNotifier, EngineHubNotifier>();

    // ── OpenAPI / Scalar (T023) ─────────────────────────────────────────────
    builder.Services.AddOpenApi();

    // ── Razor Pages for /diag (T022) ────────────────────────────────────────
    builder.Services.AddRazorPages();

    // ── CORS (dev: allow Blazor WASM dev server) ────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var u) &&
                    (u.Host == "localhost" || u.Host == "127.0.0.1"))
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // ── Memory Cache ────────────────────────────────────────────────────────
    builder.Services.AddMemoryCache();

    var app = builder.Build();

    // ── Start FFmpeg render worker ───────────────────────────────────────────
    app.Services.GetRequiredService<FFmpegRenderService>().StartWorker();

    // ── Developer Exception Page (T014) ─────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseCors();

    // ── Static files (non-fingerprinted assets from wwwroot) ────────────────
    // UseBlazorFrameworkFiles() is removed — in .NET 10 the Blazor framework
    // files are served as StaticWebAssets via MapStaticAssets() below.
    app.UseStaticFiles();

    // ── Scalar OpenAPI UI (T023) ─────────────────────────────────────────────
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar");

    if (!app.Environment.IsDevelopment())
        app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Serilog request logging ──────────────────────────────────────────────
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("UserId", httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous");
            diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
        };
    });

    // ── SignalR Hub (T020) ───────────────────────────────────────────────────
    app.MapHub<EngineHub>("/hubs/engine");

    // ── Razor Pages /diag (T022) ─────────────────────────────────────────────
    app.MapRazorPages();

    // ── Health endpoint (T021) ───────────────────────────────────────────────
    app.MapHealthEndpoint();

    // ── Config endpoint (T024b) ──────────────────────────────────────────────
    app.MapConfigEndpoints();

    // ── Ingestion endpoints (T027–T029) ──────────────────────────────────────
    app.MapIngestionEndpoints();
    // ── Processing endpoints (T046) ──────────────────────────────────────
    app.MapProcessingEndpoints();

    // ── Browser LLM result callback — browser POSTs Transformers.js output here
    app.MapPost("/api/processing/sessions/{sessionId:guid}/browser-director-result",
        (Guid sessionId,
         BrowserDirectorResultDto result,
         BrowserLLMDirectorService svc) =>
            svc.TryResolve(sessionId, result)
                ? Results.NoContent()
                : Results.NotFound(new { error = $"No pending BrowserLLM inference for session {sessionId}." }))
        .WithName("BrowserDirectorResult")
        .WithTags("Processing")
        .AllowAnonymous();

    // ── MemeLibrary endpoints (T047) ──────────────────────────────────────
    app.MapMemeLibraryEndpoints();

    // ── Output endpoints (T062–T065) ──────────────────────────────────────
    app.MapOutputEndpoints();

    // ── Admin endpoints ───────────────────────────────────────────────────
    app.MapAdminEndpoints();

    // ── Auth endpoints (T077, T078) ──────────────────────────────────────────
    app.MapAuthEndpoints();            // GET /api/auth/me
    app.MapAnonAuthEndpoints(app.Environment);  // POST /auth/anon (dev-only)

    app.MapGet("/auth/login/microsoft", async (HttpContext ctx) =>
    {
        var hasOidc = ctx.RequestServices
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme)
            .GetAwaiter().GetResult() is not null;

        if (hasOidc)
        {
            await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = "/" });
        }
        else
        {
            // Azure AD not configured — redirect to login page
            ctx.Response.Redirect("/login");
        }
    }).AllowAnonymous();

    app.MapGet("/auth/callback", () => Results.Redirect("/"))
        .AllowAnonymous();

    app.MapPost("/auth/logout", async (HttpContext ctx) =>
    {
        var provider = ctx.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        var hasOidc = await provider.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme) is not null;

        if (hasOidc)
            await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    }).AllowAnonymous();

    // ── Static web assets (fingerprinted URLs) ────────────────────────────────
    app.MapStaticAssets();

    // ── Blazor WASM SPA fallback ─────────────────────────────────────────────
    app.MapFallbackToFile("index.html");

    // ── Dev: configure Azurite CORS so browser direct-upload works ──────────
    if (app.Environment.IsDevelopment())
    {
        var blobFactory = app.Services.GetRequiredService<BlobServiceClientFactory>();
        var allowedOrigins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:8000", "http://127.0.0.1:8000", "http://localhost:5280"];
        await blobFactory.EnsureDevCorsAsync(string.Join(",", allowedOrigins));
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PoMemeVideo API terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }

/// <summary>
/// Loads only secrets prefixed with "PoMemeVideo--" and maps them to
/// configuration keys with the prefix stripped (e.g. PoMemeVideo--AzureOpenAI--Key → AzureOpenAI:Key).
/// </summary>
internal sealed class PrefixKeyVaultSecretManager : KeyVaultSecretManager
{
    private readonly string _prefix;

    public PrefixKeyVaultSecretManager(string prefix) => _prefix = prefix + "--";

    public override bool Load(SecretProperties secret)
        => secret.Name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);

    public override string GetKey(KeyVaultSecret secret)
        => secret.Name[_prefix.Length..].Replace("--", ConfigurationPath.KeyDelimiter);
}

