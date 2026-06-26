using Microsoft.AspNetCore.Mvc.RazorPages;
using PoMemeVideo.Api.AzureStorage;

namespace PoMemeVideo.Api.Pages;

public class DiagModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly AzureTableClientFactory _tableFactory;
    private readonly BlobServiceClientFactory _blobFactory;

    public List<ConnectionCheck> ConnectionChecks { get; } = [];
    public List<ConfigEntry> ConfigEntries { get; } = [];

    public DiagModel(
        IConfiguration configuration,
        AzureTableClientFactory tableFactory,
        BlobServiceClientFactory blobFactory)
    {
        _configuration = configuration;
        _tableFactory = tableFactory;
        _blobFactory = blobFactory;
    }

    public async Task OnGetAsync()
    {
        // Table Storage check
        try
        {
            _tableFactory.GetTableClient("HealthCheck");
            ConnectionChecks.Add(new("Azure Table Storage", "Connected", true));
        }
        catch (Exception ex)
        {
            ConnectionChecks.Add(new("Azure Table Storage", $"Failed: {ex.Message}", false));
        }

        // Blob Storage check
        try
        {
            var container = _blobFactory.GetClient().GetBlobContainerClient("sessions");
            await container.ExistsAsync();
            ConnectionChecks.Add(new("Azure Blob Storage", "Connected", true));
        }
        catch (Exception ex)
        {
            ConnectionChecks.Add(new("Azure Blob Storage", $"Failed: {ex.Message}", false));
        }

        // Azure AI Vision
        var visionEndpoint = _configuration["AzureAiVision:Endpoint"];
        ConnectionChecks.Add(new("Azure AI Vision",
            string.IsNullOrWhiteSpace(visionEndpoint) ? "Not configured" : "Configured",
            !string.IsNullOrWhiteSpace(visionEndpoint)));

        // Configuration key names (masked values)
        var sensitiveKeys = new[]
        {
            "KeyVault:Uri",
            "ConnectionStrings:AzureTableStorage",
            "ConnectionStrings:AzureBlobStorage",
            "AzureAiVision:Endpoint",
            "AzureAiVision:Key",
            "AzureOpenAI:Endpoint",
            "AzureOpenAI:Key",
            "AzureAd:ClientId",
            "AzureAd:TenantId",
            "AzureAd:ClientSecret",
            "ApplicationInsights:ConnectionString",
            "OpenTelemetry:Endpoint",
            "UseMockAI"
        };

        foreach (var key in sensitiveKeys)
        {
            var value = _configuration[key];
            ConfigEntries.Add(new(key, MaskValue(value)));
        }
    }

    private static string MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(not set)";
        if (value.Length <= 6) return "***";
        return $"{value[..3]}***{value[^3..]}";
    }

    public record ConnectionCheck(string Name, string Status, bool IsHealthy);
    public record ConfigEntry(string Key, string MaskedValue);
}
