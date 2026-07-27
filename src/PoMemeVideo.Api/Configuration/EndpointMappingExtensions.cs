using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using PoMemeVideo.Api.Endpoints;
using PoMemeVideo.Api.Features.Admin;
using PoMemeVideo.Api.Features.Auth;
using PoMemeVideo.Api.Features.Config;
using PoMemeVideo.Api.Features.Ingestion;
using PoMemeVideo.Api.Features.MemeLibrary;
using PoMemeVideo.Api.Features.Output;
using PoMemeVideo.Api.Features.Processing;
using PoMemeVideo.Api;
using PoMemeVideo.Api.Hubs;
using PoMemeVideo.Shared;
using Scalar.AspNetCore;
using Serilog;

namespace PoMemeVideo.Api.Configuration;

internal static class EndpointMappingExtensions
{
    public static void UseAndMapPoMemeVideo(this WebApplication app)
    {
        // Must run first: rewrites scheme/host from proxy headers before auth builds redirect URIs.
        app.UseForwardedHeaders();

        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();

        // Single-origin topology: WASM is served same-origin by this API — no CORS middleware.

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

        // ── API docs gate: restrict Scalar/OpenAPI to authenticated users in non-dev ──
        if (!app.Environment.IsDevelopment())
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path;
                if ((path.StartsWithSegments("/scalar") || path.StartsWithSegments("/openapi"))
                    && context.User?.Identity?.IsAuthenticated != true)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Unauthorized — sign in to access API documentation.");
                    return;
                }
                await next();
            });
        }

        // Header-driven test identity takes precedence over the dev ANON fallback, so a suite can
        // pin an exact user/roles pair. Non-Production only — the scheme is not registered in Production.
        if (!app.Environment.IsProduction())
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey(FakeAuthHandler.UserHeader))
                {
                    var result = await context.AuthenticateAsync(FakeAuthHandler.SchemeName);
                    if (result.Succeeded)
                        context.User = result.Principal;
                }

                await next();
            });
        }

        if (app.Environment.IsDevelopment())
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path;
                var excluded = path.StartsWithSegments("/auth")
                    || path.StartsWithSegments("/health")
                    // Realtime transport must never DEFINE the user. SignalR's negotiate can
                    // arrive without the auth cookie; minting an ANON identity here would
                    // SignInAsync over the browser's existing cookie and silently swap the user
                    // mid-session. Because userId is the VideoSessions PartitionKey, every
                    // in-flight session then 404s ("Session ... not found") on the next call.
                    || path.StartsWithSegments("/hubs")
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
            const string sessionCookieName = CorrelationHeaders.SessionCookieName;
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

            // Echo the correlation id so a browser/E2E failure can be tied straight to server logs.
            var correlationId = CorrelationPropagationHandler.ResolveCorrelationId(context);
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[CorrelationHeaders.CorrelationId] = correlationId;
                return Task.CompletedTask;
            });

            await next();
        });

        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("UserId", httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous");
                diagnosticContext.Set(
                    "SessionId",
                    CorrelationPropagationHandler.ResolveSessionId(httpContext) ?? httpContext.TraceIdentifier);
                diagnosticContext.Set(
                    "CorrelationId",
                    CorrelationPropagationHandler.ResolveCorrelationId(httpContext));
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

        // The hub carries no user-scoped data — it only relays progress into groups keyed by
        // sessionId, and never reads Context.User. Ownership is enforced on the /api endpoints
        // that create and drive a session. It must stay anonymous: under the deny-by-default
        // FallbackPolicy a negotiate without the auth cookie would 401 and the client would
        // silently lose all live progress updates.
        app.MapHub<EngineHub>("/hubs/engine").AllowAnonymous();

        // /diag is deliberately anonymous: it is a deploy-time smoke target and every value it
        // renders is masked (see DiagModel.MaskValue). Without this it would inherit the
        // deny-by-default FallbackPolicy and the post-deploy health gate would fail on a 302.
        app.MapRazorPages().AllowAnonymous();

        app.MapHealthEndpoint();
        app.MapConfigEndpoints();
        app.MapIngestionEndpoints();
        app.MapProcessingEndpoints();

        app.MapPost("/api/processing/sessions/{sessionId:guid}/browser-director-result",
            (SessionId sessionId,
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

        // The WASM shell and its framework assets must stay anonymous — they are what renders the
        // /login page in the first place. Authorization is enforced on the /api surface behind them.
        app.MapStaticAssets().AllowAnonymous();
        app.MapFallbackToFile("index.html").AllowAnonymous();
    }

    public static async Task ConfigureStorageCorsAsync(this WebApplication app)
    {
        // Browser direct-upload (a SAS PUT straight to Blob Storage) only works if the storage
        // account has a CORS rule listing the page's origin. This MUST run in EVERY environment:
        // in Production the deployed site fails uploads with "Failed to fetch" when its origin is
        // missing from the blob CORS allow-list. (This was previously gated to Development, so the
        // production origin was never configured.)
        var blobFactory = app.Services.GetRequiredService<BlobServiceClientFactory>();

        var origins = (app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .ToList();

        if (app.Environment.IsDevelopment())
        {
            // Local dev always permits the fixed dev hosts (Azurite direct-upload), regardless of config.
            origins.AddRange([
                "http://localhost:6969", "http://127.0.0.1:6969",
                "http://localhost:7000", "http://127.0.0.1:7000",
                "https://localhost:5001", "http://localhost:5280",
            ]);
        }

        var allowedOrigins = origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (allowedOrigins.Length > 0)
            await blobFactory.EnsureUploadCorsAsync(string.Join(",", allowedOrigins));
        else
            Log.Warning("No Cors:AllowedOrigins configured — browser direct-upload to Blob Storage will fail.");

        // ── Dev-only bootstrap below: restore AI selection + ensure/auto-seed the sound library ──
        if (!app.Environment.IsDevelopment())
            return;

        // Restore previously-selected AI provider so users don't have to re-click "Apply Model" after restart.
        var aiSettings = app.Services.GetRequiredService<RuntimeAiSettings>();
        ConfigEndpoints.RestoreSettings(aiSettings);

        // Ensure SoundAssets table exists and warn if empty so developers know to seed it.
        var tableFactory = app.Services.GetRequiredService<AzureTableClientFactory>();
        try
        {
            var soundsTable = tableFactory.GetTableClient(StorageNames.Tables.SoundAssets);
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
