using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoMemeVideo.Api.Features.Admin;
using PoMemeVideo.Api.Features.Auth;
using PoMemeVideo.Api.Features.Config;
using PoMemeVideo.Api.Features.Ingestion;
using PoMemeVideo.Api.Features.MemeLibrary;
using PoMemeVideo.Api.Features.Output;
using PoMemeVideo.Api.Features.Processing;
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

        // App Service terminates TLS at the front end; honor X-Forwarded-Proto so OIDC
        // builds https redirect URIs (matching the app registration), not http.
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

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
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
                else if (builder.Environment.IsDevelopment())
                {
                    // Auto-wire to .NET Aspire dashboard in Development (default OTLP gRPC port).
                    tracing.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri("http://localhost:4317");
                        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    });
                }
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
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationPropagationHandler>();

        builder.Services.AddHttpClient("Ollama")
            .AddHttpMessageHandler<CorrelationPropagationHandler>()
            .AddStandardResilienceHandler();
        builder.Services.AddHttpClient("AiFoundry")
            .AddHttpMessageHandler<CorrelationPropagationHandler>()
            .AddStandardResilienceHandler();
        builder.Services.AddSingleton<IAiVisionService, AzureOpenAiVisionService>();
        builder.Services.AddSingleton<AzureOpenAiDirectorService>();
        builder.Services.AddSingleton<AiFoundryDirectorService>();
        builder.Services.AddSingleton<OllamaDirectorService>();
        builder.Services.AddSingleton<ILocalModelCatalog>(sp => sp.GetRequiredService<OllamaDirectorService>());
        builder.Services.AddSingleton<BrowserLLMDirectorService>();
        builder.Services.AddSingleton<IDirectorService, SwitchingDirectorService>();

        builder.Services.AddScoped<SemanticMatchingService>();
        builder.Services.AddScoped<ISemanticMatchingService>(sp => sp.GetRequiredService<SemanticMatchingService>());
        builder.Services.AddScoped<RunEngineCommand>();
        builder.Services.AddScoped<RenderVideoCommand>();
        builder.Services.AddScoped<IRenderVideoCommand>(sp => sp.GetRequiredService<RenderVideoCommand>());
        builder.Services.AddSingleton<FoundryDeploymentLister>();
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

        // Cookie is always the default auth scheme. The dev-mode ANON middleware
        // and /auth/guest call SignInAsync(Cookie); if OIDC were default, UseAuthentication
        // would not read the cookie on the next request and the user would get a fresh
        // ANON identity on every call — breaking session-scoped repositories.
        // AddMicrosoftIdentityWebApp internally registers a Cookie scheme named "Cookies",
        // so we only call AddCookie ourselves when AzureAd is NOT configured.
        var authBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);

        if (hasAzureAd)
        {
            if (!hasTenantId)
            {
                Log.Warning("AzureAd:ClientId is set but AzureAd:TenantId is missing. Entra ID sign-in may fail.");
            }

            authBuilder.AddMicrosoftIdentityWebApp(azureAdSection);
        }
        else
        {
            authBuilder.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
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

        // Deny-by-default: any endpoint that does not opt out via AllowAnonymous (or state its
        // own policy) requires an authenticated user. This closes endpoints that simply forgot
        // to call RequireAuthorization — the BFF's authorization posture is now opt-out, not opt-in.
        // Header-driven test identity (X-Fake-User / X-Fake-Roles). Registered outside Production
        // only; FakeAuthHandler itself also throws if it is ever constructed in Production.
        if (!builder.Environment.IsProduction())
        {
            authBuilder.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, FakeAuthHandler>(
                FakeAuthHandler.SchemeName, _ => { });
        }

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

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
                    var dpContainer = new BlobContainerClient(blobConn, StorageNames.Containers.DataProtection);
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

        // HybridCache over MemoryCache: adds stampede protection on the sound-library read,
        // and leaves a seam for a distributed L2 without touching call sites.
        builder.Services.AddMemoryCache();
        builder.Services.AddHybridCache();
    }
}
