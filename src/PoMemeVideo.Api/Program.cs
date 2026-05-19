using Azure.Extensions.AspNetCore.Configuration.Secrets;
using PoMemeVideo.Infrastructure;
using Azure.Security.KeyVault.Secrets;
using PoMemeVideo.Api.Configuration;
using PoMemeVideo.Api.Features.Admin;
using PoMemeVideo.Infrastructure.AzureStorage;
using PoMemeVideo.Infrastructure.FFmpeg;
using PoMemeVideo.Shared;
using Serilog;
using Serilog.Events;

// ── CLI verb: dotnet run -- seed-sounds [--seeds-dir <path>] ─────────────────
// Short-circuits before web host construction so no Azure auth/storage is needed.
if (args.Length > 0 && args[0] == "seed-sounds")
{
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    return await SeedSoundsCommand.RunAsync(args[1..], config);
}

// Bootstrap logger for startup errors
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.ConfigurePoMemeVideoConfiguration();
    builder.ConfigurePoMemeVideoSerilog();
    builder.AddPoMemeVideoServices();

    var app = builder.Build();

    app.UseExceptionHandler();

    // ── Start FFmpeg render worker ───────────────────────────────────────────
    app.Services.GetRequiredService<FFmpegRenderService>().StartWorker();

    app.UseAndMapPoMemeVideo();
    await app.ConfigureDevStorageCorsAsync();

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

/// <summary>
/// Loads app-prefixed secrets (PoMemeVideo--) and shared un-prefixed secrets.
/// App-specific secrets strip the prefix (PoMemeVideo--AzureOpenAI--Key -> AzureOpenAI:Key),
/// while shared secrets map directly (AzureAd--TenantId -> AzureAd:TenantId).
/// </summary>
internal sealed class PrefixKeyVaultSecretManager : KeyVaultSecretManager
{
    private readonly string _prefix;

    public PrefixKeyVaultSecretManager(string prefix) => _prefix = prefix + "--";

    public override bool Load(SecretProperties secret)
    {
        if (secret.Name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        var firstSeparator = secret.Name.IndexOf("--", StringComparison.Ordinal);
        if (firstSeparator <= 0)
            return true;

        var prefixCandidate = secret.Name[..firstSeparator];
        return !prefixCandidate.StartsWith("Po", StringComparison.OrdinalIgnoreCase);
    }

    public override string GetKey(KeyVaultSecret secret)
    {
        var name = secret.Name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase)
            ? secret.Name[_prefix.Length..]
            : secret.Name;
        return name.Replace("--", ConfigurationPath.KeyDelimiter);
    }
}

