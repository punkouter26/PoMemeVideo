using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoMemeVideo.Api.Configuration;
using PoMemeVideo.Api.Endpoints;
using PoMemeVideo.Api.Features.Config;
using PoMemeVideo.Api.Features.Ingestion;
using PoMemeVideo.Api.Hubs;
using PoMemeVideo.Application.Ingestion;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure.AzureStorage;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

// Bootstrap logger for startup errors
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

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

    // ── Feature Flags (T018) ────────────────────────────────────────────────
    builder.Services.Configure<FeatureFlags>(
        builder.Configuration.GetSection(FeatureFlags.SectionName));

    // ── Azure Storage (T016, T017, T017b) ──────────────────────────────────
    builder.Services.AddAzureTableClientFactory();
    builder.Services.AddBlobServiceClientFactory();
    builder.Services.AddBlobStorageService();

    // ── Ingestion (T025, T026) ───────────────────────────────────────────
    builder.Services.AddVideoSessionTableRepository();
    builder.Services.AddScoped<IngestVideoCommand>();

    // ── Authentication (T019) ───────────────────────────────────────────────
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/auth/login/microsoft";
            options.LogoutPath = "/auth/logout";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
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

    // ── Developer Exception Page (T014) ─────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseCors();

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

    // ── Auth stubs (T019) ────────────────────────────────────────────────────
    // ANON login handled in AnonAuthHandler (Phase 7, T077) — stub returns 501 for now
    app.MapPost("/auth/anon", () => Results.StatusCode(501))
        .AllowAnonymous();

    app.MapGet("/auth/login/microsoft", () => Results.Redirect("/"))
        .AllowAnonymous();

    app.MapGet("/auth/callback", () => Results.Redirect("/"))
        .AllowAnonymous();

    app.MapPost("/auth/logout", async (HttpContext ctx) =>
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    });

    // ── Dev: configure Azurite CORS so browser direct-upload works ──────────
    if (app.Environment.IsDevelopment())
    {
        var blobFactory = app.Services.GetRequiredService<BlobServiceClientFactory>();
        var allowedOrigins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5280"];
        foreach (var origin in allowedOrigins)
            await blobFactory.EnsureDevCorsAsync("*");
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

