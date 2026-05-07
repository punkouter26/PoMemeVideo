using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Serilog;
using Serilog.Events;

namespace PoMemeVideo.Api.Configuration;

internal static class StartupConfigurationExtensions
{
    public static void ConfigurePoMemeVideoConfiguration(this WebApplicationBuilder builder)
    {
        var kvUri = builder.Configuration["KeyVault:Uri"]
                    ?? "https://kv-poshared.vault.azure.net/";
        TokenCredential credential = builder.Environment.IsDevelopment()
            ? new AzureCliCredential()
            : new DefaultAzureCredential();

        builder.Configuration.AddAzureKeyVault(
            new SecretClient(new Uri(kvUri), credential),
            new PrefixKeyVaultSecretManager("PoMemeVideo"));

        if (!builder.Environment.IsDevelopment())
            return;

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

    public static void ConfigurePoMemeVideoSerilog(this WebApplicationBuilder builder)
    {
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
    }
}
