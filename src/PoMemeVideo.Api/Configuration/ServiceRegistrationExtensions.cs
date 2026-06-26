using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoMemeVideo.Api.Features.Processing;
using PoMemeVideo.Api.Infrastructure;
using PoMemeVideo.Application.Ingestion;
using PoMemeVideo.Application.MemeLibrary;
using PoMemeVideo.Application.Processing;
using PoMemeVideo.Application.Rendering;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Api.Hubs;
using PoMemeVideo.Infrastructure;
using PoMemeVideo.Infrastructure.AiFoundry;
using PoMemeVideo.Infrastructure.AzureOpenAi;
using PoMemeVideo.Infrastructure.AzureStorage;
using PoMemeVideo.Infrastructure.BrowserLlm;
using PoMemeVideo.Infrastructure.FFmpeg;
using PoMemeVideo.Infrastructure.Ollama;
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
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("PoMemeVideo.*");

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

        builder.Services.AddSingleton<RuntimeAiSettings>();

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

        builder.Services.AddMemoryCache();
    }
}
