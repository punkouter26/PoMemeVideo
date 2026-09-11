using System.Net;
using System.Text;
using Azure.Storage.Blobs;

namespace PoMemeVideo.Api.Common;

/// <summary>
/// Deploy-time smoke panel at <c>/diag</c>.
///
/// This was a Razor Page, which meant the whole Razor Pages subsystem — AddRazorPages,
/// MapRazorPages, a Pages/ folder and a PageModel — existed to render one static table.
/// A minimal-API endpoint writing the same markup costs one file and no framework surface.
/// </summary>
public static class DiagEndpoint
{
    // Rendered with their values masked, so the panel can stay anonymous.
    private static readonly string[] SensitiveKeys =
    [
        "KeyVault:Uri",
        "ConnectionStrings:AzureTableStorage",
        "ConnectionStrings:AzureBlobStorage",
        "AzureAiVision:Endpoint",
        "AzureAiVision:Key",
        "AiFoundry:Endpoint",
        "AiFoundry:Key",
        // Legacy aliases, still honoured as a fallback by AiFoundryVisionService.
        "AzureOpenAI:Endpoint",
        "AzureOpenAI:Key",
        "AzureAd:ClientId",
        "AzureAd:TenantId",
        "AzureAd:ClientSecret",
        "ApplicationInsights:ConnectionString",
        "OpenTelemetry:Endpoint",
        "UseMockAI",
    ];

    public static IEndpointRouteBuilder MapDiagEndpoint(this IEndpointRouteBuilder app)
    {
        // Deliberately anonymous: it is a deploy-time smoke target and every value it renders is
        // masked. Without this it would inherit the deny-by-default FallbackPolicy and the
        // post-deploy health gate would fail on a 302.
        app.MapGet("/diag", async (
            IConfiguration configuration,
            AzureTableClientFactory tableFactory,
            BlobServiceClientFactory blobFactory,
            CancellationToken ct) =>
        {
            var checks = new List<(string Name, string Status, bool Ok)>();

            try
            {
                tableFactory.GetTableClient(StorageNames.Tables.HealthCheck);
                checks.Add(("Azure Table Storage", "Connected", true));
            }
            catch (Exception ex)
            {
                checks.Add(("Azure Table Storage", $"Failed: {ex.Message}", false));
            }

            try
            {
                var container = blobFactory.GetClient()
                    .GetBlobContainerClient(StorageNames.Containers.Sessions);
                await container.ExistsAsync(ct);
                checks.Add(("Azure Blob Storage", "Connected", true));
            }
            catch (Exception ex)
            {
                checks.Add(("Azure Blob Storage", $"Failed: {ex.Message}", false));
            }

            var visionEndpoint = configuration["AzureAiVision:Endpoint"];
            var visionConfigured = !string.IsNullOrWhiteSpace(visionEndpoint);
            checks.Add(("Azure AI Vision", visionConfigured ? "Configured" : "Not configured", visionConfigured));

            return Results.Content(Render(checks, configuration), "text/html; charset=utf-8");
        })
        .WithName("Diagnostics")
        .WithTags("Diagnostics")
        .ExcludeFromDescription()
        .AllowAnonymous();

        return app;
    }

    private static string Render(
        List<(string Name, string Status, bool Ok)> checks,
        IConfiguration configuration)
    {
        var sb = new StringBuilder(4096);

        sb.Append("""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="UTF-8" />
            <title>PoMemeVideo — Diagnostics</title>
            <style>
            body { background: #000; color: #00FF41; font-family: 'Courier New', monospace; padding: 2rem; margin: 0; }
            h2 { font-size: 1rem; margin: 1.5rem 0 0.5rem; color: #00FF41;
                 border: 1px solid #00FF41; padding: 0.25rem 0.75rem; display: inline-block; }
            table { border-collapse: collapse; width: 100%; margin-bottom: 1rem; }
            th, td { border: 1px solid #00FF41; padding: 0.4rem 0.8rem; text-align: left; font-size: 0.85rem; }
            th { color: #fff; background: #001a00; }
            tr:hover td { background: #001a00; }
            .ok { color: #00FF41; }
            .degraded { color: #FF4141; }
            pre { margin: 0; color: #00FF41; line-height: 1.3; }
            a { color: #00FF41; }
            </style>
            </head>
            <body>
            <pre>
            ╔══════════════════════════════════════════╗
            ║    POMEMEVIDEO :: DIAGNOSTIC PANEL       ║
            ╚══════════════════════════════════════════╝
            </pre>
            <h2>[ EXTERNAL CONNECTIONS ]</h2>
            <table><thead><tr><th>Service</th><th>Status</th></tr></thead><tbody>
            """);

        foreach (var (name, status, ok) in checks)
        {
            sb.Append("<tr><td>").Append(Encode(name))
              .Append("</td><td class=\"").Append(ok ? "ok" : "degraded").Append("\">")
              .Append(Encode(status)).Append("</td></tr>");
        }

        sb.Append("""
            </tbody></table>
            <h2>[ CONFIGURATION KEYS ]</h2>
            <table><thead><tr><th>Key</th><th>Value</th></tr></thead><tbody>
            """);

        foreach (var key in SensitiveKeys)
        {
            sb.Append("<tr><td>").Append(Encode(key))
              .Append("</td><td>").Append(Encode(Mask(configuration[key])))
              .Append("</td></tr>");
        }

        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    // Config values reach the page from Key Vault and environment, so they are encoded even
    // though they are also masked — a value carrying markup must never render as markup.
    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(not set)";
        return value.Length <= 6 ? "***" : $"{value[..3]}***{value[^3..]}";
    }
}
