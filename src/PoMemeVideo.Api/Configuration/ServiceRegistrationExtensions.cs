using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Identity.Web;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoMemeVideo.Api.Hubs;
using PoMemeVideo.Shared;
using Serilog;

namespace PoMemeVideo.Api.Configuration;

internal static class ServiceRegistrationExtensions
{
    public static void AddPoMemeVideoServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("PoMemeVideo"))
            .WithTracing(tracing =>
            {
                tracing
                    // Strip health/liveness probe noise from traces (telemetry budget).
                    .AddAspNetCoreInstrumentation(o =>
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()
                    .AddSource("PoMemeVideo.*");

                // Full capture in dev/test; fixed-rate 10% sampling in prod to cap ingestion cost.
                tracing.SetSampler(builder.Environment.IsDevelopment()
                    ? new AlwaysOnSampler()
                    : new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)));

                var otlpEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        builder.Services.AddAzureTableClientFactory();
        builder.Services.AddBlobServiceClientFactory();
        builder.Services.AddBlobStorageService();
        builder.Services.AddUserIdentityTableRepository();

        builder.Services.AddVideoSessionTableRepository();
        builder.Services.AddScoped<IngestVideoCommand>();

        builder.Services.AddSoundAssetTableRepository();
        builder.Services.AddDirectorScriptTableRepository();

        builder.Services.AddSingleton(new RuntimeAiSettings
        {
            // Prod has no local Ollama/WebGPU runtime — default to the cloud director.
            Provider = builder.Environment.IsDevelopment() ? "BrowserLLM" : "AzureOpenAI",
        });

        // Typed/named HttpClients backed by a standard resilience pipeline
        // (retry + timeout + circuit breaker) per the .NET 10 resilience mandate.
        builder.Services.AddHttpClient("Ollama").AddStandardResilienceHandler();
        builder.Services.AddHttpClient("AiFoundry").AddStandardResilienceHandler();
        builder.Services.AddSingleton<IAiVisionService, AzureOpenAiVisionService>();
        builder.Services.AddSingleton<AzureOpenAiDirectorService>();
        builder.Services.AddSingleton<AiFoundryDirectorService>();
        builder.Services.AddSingleton<OllamaDirectorService>();
        builder.Services.AddSingleton<BrowserLLMDirectorService>();
        builder.Services.AddSingleton<IDirectorService, SwitchingDirectorService>();

        builder.Services.AddScoped<SemanticMatchingService>();
        builder.Services.AddScoped<RunEngineCommand>();
        builder.Services.AddScoped<RenderVideoCommand>();
        builder.Services.AddSingleton<EngineRunDispatcher>();
        builder.Services.AddSingleton<IEngineRunDispatcher>(sp => sp.GetRequiredService<EngineRunDispatcher>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<EngineRunDispatcher>());
        builder.Services.AddSingleton<FFmpegRenderService>();
        builder.Services.AddSingleton<IVideoRenderService>(sp => sp.GetRequiredService<FFmpegRenderService>());

        var azureAdSection = builder.Configuration.GetSection("AzureAd");
        var hasAzureAd = !string.IsNullOrWhiteSpace(azureAdSection["ClientId"]);
        var hasTenantId = !string.IsNullOrWhiteSpace(azureAdSection["TenantId"]);
        var hasClientSecret = !string.IsNullOrWhiteSpace(azureAdSection["ClientSecret"]);

        Log.Information(
            "Authentication setup. Mode={Mode}, HasAzureAdClientId={HasClientId}, HasAzureAdTenantId={HasTenantId}, HasAzureAdClientSecret={HasClientSecret}",
            hasAzureAd ? "EntraId-OIDC" : "CookieOnly",
            hasAzureAd,
            hasTenantId,
            hasClientSecret);

        if (hasAzureAd)
        {
            if (!hasTenantId)
            {
                Log.Warning("AzureAd:ClientId is set but AzureAd:TenantId is missing. Entra ID sign-in may fail.");
            }

            builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(azureAdSection);
        }
        else
        {
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.LogoutPath = "/auth/logout";
                    options.Cookie.HttpOnly = true;
                    // BFF session cookie: encrypted by the data-protection layer, HttpOnly,
                    // SameSite=Strict, and Secure everywhere except local http dev.
                    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                        ? CookieSecurePolicy.SameAsRequest
                        : CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                });
        }

        builder.Services.AddAuthorization();

        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IEngineNotifier, EngineHubNotifier>();

        builder.Services.AddOpenApi();
        builder.Services.AddRazorPages();

        // Single-origin: the WASM client is served same-origin by this API, so no CORS.

        // Data Protection: persist keys to Blob so BFF auth cookies survive container
        // restarts/redeploys. Dev keeps the default (ephemeral local) provider.
        if (!builder.Environment.IsDevelopment())
        {
            var blobConn = builder.Configuration.GetConnectionString("AzureBlobStorage");
            if (!string.IsNullOrWhiteSpace(blobConn))
            {
                try
                {
                    var dpContainer = new BlobContainerClient(blobConn, "dataprotection");
                    dpContainer.CreateIfNotExists();
                    builder.Services.AddDataProtection()
                        .SetApplicationName(PoMemeVideoNaming.ApplicationName)
                        .PersistKeysToAzureBlobStorage(dpContainer.GetBlobClient("keys.xml"));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Data Protection blob persistence unavailable; using default key store.");
                }
            }
        }

        builder.Services.AddMemoryCache();
    }
}
