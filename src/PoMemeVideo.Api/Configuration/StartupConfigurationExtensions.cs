using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using PoMemeVideo.Shared;
using Serilog;
using Serilog.Events;

namespace PoMemeVideo.Api.Configuration;

internal static class StartupConfigurationExtensions
{
    public static void ConfigurePoMemeVideoConfiguration(this WebApplicationBuilder builder)
    {
        Log.Information(
            "Startup configuration begin. Environment={Environment}, ContentRoot={ContentRoot}",
            builder.Environment.EnvironmentName,
            builder.Environment.ContentRootPath);

        var kvUri = builder.Configuration["KeyVault:Uri"];
        if (string.IsNullOrWhiteSpace(kvUri) && !builder.Environment.IsDevelopment())
        {
            kvUri = PoMemeVideoNaming.FallbackSharedKeyVaultUri;
            Log.Information(
                "No explicit KeyVault:Uri configured for non-development environment. Falling back to shared URI={KeyVaultUri}",
                kvUri);
        }

        if (!string.IsNullOrWhiteSpace(kvUri))
        {
            TokenCredential credential = builder.Environment.IsDevelopment()
                ? new AzureCliCredential()
                : new DefaultAzureCredential();

            var credentialMode = builder.Environment.IsDevelopment()
                ? nameof(AzureCliCredential)
                : nameof(DefaultAzureCredential);

            try
            {
                builder.Configuration.AddAzureKeyVault(
                    new SecretClient(new Uri(kvUri), credential),
                    new PrefixKeyVaultSecretManager(PoMemeVideoNaming.SecretPrefix));

                Log.Information(
                    "Azure Key Vault provider added. Uri={KeyVaultUri}, CredentialMode={CredentialMode}, SecretPrefix={SecretPrefix}",
                    kvUri,
                    credentialMode,
                    PoMemeVideoNaming.SecretPrefix);
            }
            catch (Exception ex)
            {
                // Keep startup alive and surface high-signal telemetry for environment-specific auth faults.
                Log.Error(
                    ex,
                    "Failed to add Azure Key Vault provider. Uri={KeyVaultUri}, CredentialMode={CredentialMode}. Startup will continue with remaining config sources.",
                    kvUri,
                    credentialMode);
            }
        }
        else
        {
            Log.Warning("Azure Key Vault provider not configured because no KeyVault:Uri is available.");
        }

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
        {
            builder.Configuration.AddInMemoryCollection(overrideDict);
            Log.Information(
                "Loaded {OverrideCount} development override key(s) from appsettings.Development.json",
                overrideDict.Count);
        }
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
                .Enrich.WithProperty("Application", PoMemeVideoNaming.ApplicationName);

            // Rolling files are a local-debugging convenience only. On App Service stdout is
            // already captured, and 30 days of retained logs eat into the F1 plan's 1 GB quota
            // that the deployment package itself is sized against.
            if (context.HostingEnvironment.IsDevelopment())
            {
                loggerConfig.WriteTo.File(
                    path: $"logs/{PoMemeVideoNaming.ApplicationSlug}-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);
            }

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
