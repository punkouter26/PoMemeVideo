using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using PoMemeVideo.Api.Endpoints;
using PoMemeVideo.Api.Features.Admin;
using PoMemeVideo.Api.Features.Auth;
using PoMemeVideo.Api.Features.Config;
using PoMemeVideo.Api;
using PoMemeVideo.Api.Features.Ingestion;
using PoMemeVideo.Api.Features.MemeLibrary;
using PoMemeVideo.Api.Features.Output;
using PoMemeVideo.Api.Features.Processing;
using PoMemeVideo.Api.Hubs;
using PoMemeVideo.Api.AzureStorage;
using PoMemeVideo.Api.BrowserLlm;
using Scalar.AspNetCore;
using Serilog;

namespace PoMemeVideo.Api.Configuration;

internal static class EndpointMappingExtensions
{
    public static void UseAndMapPoMemeVideo(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();

        app.UseCors();

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                if (app.Environment.IsDevelopment()
                    && ctx.Context.Request.Path.StartsWithSegments("/_framework"))
                {
                    ctx.Context.Response.Headers["Cache-Control"] = "no-store, no-cache";
                    ctx.Context.Response.Headers["Pragma"] = "no-cache";
                }
            }
        });

        var modelsRoot = ResolveModelsRoot(app.Environment.ContentRootPath);
        if (modelsRoot is not null)
        {
            var modelContentTypes = new FileExtensionContentTypeProvider();
            modelContentTypes.Mappings[".onnx"] = "application/octet-stream";
            modelContentTypes.Mappings[".onnx_data"] = "application/octet-stream";
            modelContentTypes.Mappings[".model"] = "application/octet-stream";

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(modelsRoot),
                RequestPath = "/models",
                ContentTypeProvider = modelContentTypes,
                ServeUnknownFileTypes = true,
            });
        }

        app.MapOpenApi();
        app.MapScalarApiReference("/scalar");

        if (!app.Environment.IsDevelopment())
            app.UseHttpsRedirection();

        app.UseAuthentication();

        if (app.Environment.IsDevelopment())
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path;
                var excluded = path.StartsWithSegments("/auth")
                    || path.StartsWithSegments("/health")
                    || path.StartsWithSegments("/scalar")
                    || path.StartsWithSegments("/openapi")
                    || path.StartsWithSegments("/api/config")
                    || path.StartsWithSegments("/models")
                    || path.StartsWithSegments("/_framework")
                    || Path.HasExtension(path);

                if (!excluded)
                    await UserIdentityResolution.EnsureDevelopmentAnonymousIdentityAsync(context);

                await next();
            });
        }

        app.UseAuthorization();

        app.Use(async (context, next) =>
        {
            const string sessionCookieName = "pmv-session-id";
            if (!context.Request.Cookies.ContainsKey(sessionCookieName))
            {
                context.Response.Cookies.Append(
                    sessionCookieName,
                    Guid.NewGuid().ToString("N"),
                    new CookieOptions
                    {
                        HttpOnly = false,
                        IsEssential = true,
                        Secure = context.Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddDays(14),
                    });
            }

            await next();
        });

        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("UserId", httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous");
                var sessionId = httpContext.Request.Headers["X-Session-Id"].FirstOrDefault()
                    ?? httpContext.Request.Cookies["pmv-session-id"]
                    ?? httpContext.TraceIdentifier;
                diagnosticContext.Set("SessionId", sessionId);
                diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
            };
            // Demote high-frequency heartbeat polls so they don't drown the log
            options.GetLevel = (httpContext, elapsed, ex) =>
            {
                var path = httpContext.Request.Path.Value ?? "";
                if (ex is null &&
                    (path == "/api/auth/me" ||
                     path == "/api/config" ||
                     path.StartsWith("/api/config/ai-model")))
                    return Serilog.Events.LogEventLevel.Debug;
                return ex is not null
                    ? Serilog.Events.LogEventLevel.Error
                    : Serilog.Events.LogEventLevel.Information;
            };
        });

        app.MapHub<EngineHub>("/hubs/engine");
        app.MapRazorPages();
        app.MapHealthEndpoint();
        app.MapConfigEndpoints();
        app.MapIngestionEndpoints();
        app.MapProcessingEndpoints();

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

        app.MapMemeLibraryEndpoints();
        app.MapOutputEndpoints();
        app.MapAdminEndpoints();

        app.MapAuthEndpoints();
        app.MapGuestAuthEndpoints(app.Environment);

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
                ctx.Response.Redirect("/login");
            }
        }).AllowAnonymous();

        app.MapGet("/auth/callback", () => Results.Redirect("/"))
            .AllowAnonymous();

        app.MapPost("/auth/logout", async (HttpContext ctx, IHostEnvironment env) =>
        {
            var provider = ctx.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            var hasOidc = await provider.GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme) is not null;

            if (hasOidc)
                await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);

            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // In Development, go back to the login page so GUEST button is available again.
            return Results.Redirect(env.IsDevelopment() ? "/login" : "/");
        }).AllowAnonymous();

        app.MapStaticAssets();
        app.MapFallbackToFile("index.html");
    }

    public static async Task ConfigureDevStorageCorsAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return;

        // Restore previously-selected AI provider so users don't have to re-click "Apply Model" after restart.
        var aiSettings = app.Services.GetRequiredService<RuntimeAiSettings>();
        ConfigEndpoints.RestoreSettings(aiSettings);

        var blobFactory = app.Services.GetRequiredService<BlobServiceClientFactory>();
        var allowedOrigins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:7000", "http://127.0.0.1:7000", "https://localhost:5001", "http://localhost:5280"];
        await blobFactory.EnsureDevCorsAsync(string.Join(",", allowedOrigins));

        // Ensure SoundAssets table exists and warn if empty so developers know to seed it.
        var tableFactory = app.Services.GetRequiredService<AzureTableClientFactory>();
        try
        {
            var soundsTable = tableFactory.GetTableClient("SoundAssets");
            var hasEntries = soundsTable.Query<Azure.Data.Tables.TableEntity>(maxPerPage: 1).Any();
            if (!hasEntries)
            {
                Log.Warning("[DEV] Sound library is empty — run: python SCRIPTS/seed-meme-sounds.py  (or: dotnet run -- seed-sounds)");
                // Attempt auto-seed in Development so the app is immediately usable.
                try
                {
                    var seedScript = Path.Combine(app.Environment.ContentRootPath, "..", "..", "..", "SCRIPTS", "seed-meme-sounds.py");
                    if (File.Exists(seedScript))
                    {
                        Log.Information("[DEV] Auto-seeding sound library from {Script}...", seedScript);
                        var psi = new System.Diagnostics.ProcessStartInfo("python", $"\"{seedScript}\"")
                        {
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        if (proc is not null)
                        {
                            var stdout = await proc.StandardOutput.ReadToEndAsync();
                            var stderr = await proc.StandardError.ReadToEndAsync();
                            await proc.WaitForExitAsync();
                            if (proc.ExitCode == 0)
                                Log.Information("[DEV] Sound library seeded successfully.");
                            else
                                Log.Warning("[DEV] Sound library seeding failed (exit {Code}): {Stderr}", proc.ExitCode, stderr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[DEV] Auto-seed attempt failed — seed manually.");
                }
            }
        }
        catch
        {
            // Non-fatal — storage may not be available at startup
        }
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
}
