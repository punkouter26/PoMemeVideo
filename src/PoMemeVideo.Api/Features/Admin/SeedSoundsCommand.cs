// SOLID: Single Responsibility — sound library seeding isolated from web host startup
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.Features.Admin;

/// <summary>
/// CLI seeder: dotnet run -- seed-sounds [--seeds-dir &lt;path&gt;]
/// Reads tools/meme-sounds/sounds-metadata.json and uploads MP3s to Azurite Blob Storage,
/// then writes SoundAsset rows to Azurite Table Storage. Idempotent — skips existing rows.
/// </summary>
public static class SeedSoundsCommand
{
    internal const string ContainerName = StorageNames.Containers.Sounds;
    internal const string TableName = StorageNames.Tables.SoundAssets;
    internal const string PartitionKey = "library";

    public static async Task<int> RunAsync(string[] args, IConfiguration config)
    {
        var seedsDir = ResolveSeedsDir(args, Directory.GetCurrentDirectory());
        return await RunForDirAsync(seedsDir, config, verbose: true);
    }

    /// <summary>
    /// Library-loading path used by both the CLI verb and the in-process HTTP seed endpoint
    /// (POST /api/memelibrary/seed). Resolves <c>seedsDir</c> relative to <paramref name="contentRoot"/>
    /// so the running web host can find the metadata no matter the working directory.
    /// </summary>
    public static async Task<int> RunForDirAsync(string seedsDir, IConfiguration config, bool verbose)
    {
        var metaFile = Path.Combine(seedsDir, "sounds-metadata.json");

        if (verbose)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════╗");
            Console.WriteLine("║  PoMemeVideo // SOUND LIBRARY SEEDER             ║");
            Console.WriteLine("╚══════════════════════════════════════════════════╝");
            Console.WriteLine();
        }

        if (!File.Exists(metaFile))
        {
            if (verbose)
            {
                Console.Error.WriteLine($"✗ Metadata file not found: {metaFile}");
                Console.Error.WriteLine("  Run tools/download-meme-sounds.py first, or pass --seeds-dir <path>.");
            }
            return 1;
        }

        // ── Parse metadata ───────────────────────────────────────────────────
        var json = await File.ReadAllTextAsync(metaFile);
        var meta = JsonSerializer.Deserialize<SoundsMetadata>(json, JsonOptions)!;
        if (verbose)
        {
            Console.WriteLine($"  Found {meta.Sounds.Count} sounds in metadata.");
            Console.WriteLine();
        }

        // ── Connect to storage ───────────────────────────────────────────────
        var connStr = config.GetConnectionString("AzureStorage")
                   ?? config["ConnectionStrings:AzureStorage"]
                   ?? "UseDevelopmentStorage=true";

        var blobServiceClient = new BlobServiceClient(connStr);
        var tableServiceClient = new Azure.Data.Tables.TableServiceClient(connStr);

        // Ensure container exists
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync();
        if (verbose) Console.WriteLine($"  ✓ Blob container '{ContainerName}' ready.");

        // Ensure table exists
        await tableServiceClient.CreateTableIfNotExistsAsync(TableName);
        var tableClient = tableServiceClient.GetTableClient(TableName);
        if (verbose) Console.WriteLine($"  ✓ Table '{TableName}' ready.");
        if (verbose) Console.WriteLine();

        int seeded = 0, skipped = 0, failed = 0;

        foreach (var entry in meta.Sounds)
        {
            var soundId = DeriveStableGuid(entry.Id ?? entry.Filename);

            // Skip only when the row AND its blob already point at our local container — i.e.
            // a previous successful run. A row whose BlobUrl still references an external
            // source (e.g. freesound.org) is *not* considered seeded and will be re-uploaded +
            // have its BlobUrl rewritten. This keeps the library coherent across format changes
            // and prevents "the row says freesound but the local blob is the one we want" splits.
            try
            {
                var existing = await tableClient.GetEntityAsync<TableEntity>(PartitionKey, soundId.ToString());
                var existingUrl = existing.Value.GetString("BlobUrl") ?? string.Empty;
                var blobExists = await containerClient.GetBlobClient(entry.Filename).ExistsAsync();
                if (blobExists && existingUrl.Contains("/devstoreaccount", StringComparison.OrdinalIgnoreCase))
                {
                    if (verbose) Console.WriteLine($"  [SKIP] {entry.DisplayName}");
                    skipped++;
                    continue;
                }
                // Row exists but the local blob is missing OR the row still points at an external URL.
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Not found — proceed with seed
            }

            // Upload blob: local file preferred, source URL fallback
            var localFile = Path.Combine(seedsDir, entry.Filename);
            string blobUrl;

            if (File.Exists(localFile))
            {
                var blobClient = containerClient.GetBlobClient(entry.Filename);
                if (!await blobClient.ExistsAsync())
                {
                    await using var stream = File.OpenRead(localFile);
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders
                    {
                        ContentType = "audio/mpeg"
                    });
                }
                blobUrl = blobClient.Uri.ToString();
            }
            else if (!string.IsNullOrWhiteSpace(entry.SourceUrl))
            {
                // Self-host: download from source URL into our blob container. Keeps the stream
                // endpoint self-contained (no outbound proxy required at runtime).
                var blobClient = containerClient.GetBlobClient(entry.Filename);
                if (!await blobClient.ExistsAsync())
                {
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                        using var req = new HttpRequestMessage(HttpMethod.Get, entry.SourceUrl);
                        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (PoMemeVideo sound seeder)");
                        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                        if (resp.IsSuccessStatusCode)
                        {
                            await using var stream = await resp.Content.ReadAsStreamAsync();
                            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = "audio/mpeg" });
                        }
                        else if (verbose)
                        {
                            Console.WriteLine($"  [WARN] HTTP {(int)resp.StatusCode} fetching {entry.SourceUrl}");
                        }
                    }
                    catch (Exception ex) when (verbose)
                    {
                        Console.WriteLine($"  [WARN] Could not download {entry.SourceUrl}: {ex.Message}");
                    }
                }

                blobUrl = blobClient.Uri.ToString();
                if (!await containerClient.GetBlobClient(entry.Filename).ExistsAsync())
                {
                    if (verbose) Console.WriteLine($"  [WARN] Blob missing after fetch, using sourceUrl metadata: {entry.Filename}");
                    blobUrl = entry.SourceUrl!;
                    failed++;
                    continue;
                }
            }
            else
            {
                if (verbose) Console.WriteLine($"  [WARN] MP3 + sourceUrl both missing: {entry.Filename}");
                failed++;
                continue;
            }

            // Insert (or upsert) table row — even on refresh we rewrite the row so the BlobUrl
            // always points at our local container.
            var entity = new TableEntity(PartitionKey, soundId.ToString())
            {
                ["DisplayName"] = entry.DisplayName,
                ["DurationMs"] = entry.DurationMs,
                ["Tags"] = string.Join(",", entry.ActionVectorTags),
                ["BlobUrl"] = blobUrl,
                ["Priority"] = entry.Priority,
            };

            try
            {
                await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
                if (verbose) Console.WriteLine($"  [SEED] {entry.DisplayName}");
                seeded++;
            }
            catch (Exception ex) when (verbose)
            {
                Console.WriteLine($"  [FAIL] {entry.DisplayName}: {ex.Message}");
                failed++;
            }
        }

        if (verbose)
        {
            Console.WriteLine();
            Console.WriteLine($"╔══════════════════════════════════════════════════╗");
            Console.WriteLine($"║  DONE — Seeded: {seeded,4} | Skipped: {skipped,4} | Failed: {failed,4}  ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════╝");
        }

        return failed > 0 ? 1 : 0;
    }

    /// <summary>
    /// Locates the directory containing <c>sounds-metadata.json</c>. Tries the explicit override,
    /// then a few well-known relative locations so the running API finds the metadata regardless
    /// of working directory.
    /// </summary>
    public static string ResolveSeedsDir(string[] args, string cwd)
    {
        var explicitDir = GetArg(args, "--seeds-dir");
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return explicitDir!;

        var candidates = new[]
        {
            Path.Combine(cwd, "tools", "meme-sounds"),
            Path.Combine(cwd, "SCRIPTS", "meme-sounds"),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "SCRIPTS", "meme-sounds")),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "tools", "meme-sounds")),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "..", "SCRIPTS", "meme-sounds")),
        };

        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "sounds-metadata.json")))
               ?? Path.Combine(cwd, "tools", "meme-sounds");
    }

    // Derives a stable UUID-v5 from the sound slug — same slug always produces the same GUID.
    private static Guid DeriveStableGuid(string slug)
    {
        // UUID v5 with DNS namespace (RFC 4122)
        var namespaceBytes = new byte[] { 0x6b, 0xa7, 0xb8, 0x10, 0x9d, 0xad, 0x11, 0xd1, 0x80, 0xb4, 0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8 };
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(slug);
        var combined = namespaceBytes.Concat(nameBytes).ToArray();
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(combined);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80); // variant RFC 4122
        return new Guid(hash[..16]);
    }

    private static string? GetArg(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── JSON deserialization models ───────────────────────────────────────────
    private sealed class SoundsMetadata
    {
        public string Version { get; set; } = "1.0";
        public List<SoundEntry> Sounds { get; set; } = [];
    }

    private sealed class SoundEntry
    {
        public string? Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public int DurationMs { get; set; }
        [JsonPropertyName("actionVectorTags")]
        public string[] ActionVectorTags { get; set; } = [];
        public bool Priority { get; set; }
    }
}
