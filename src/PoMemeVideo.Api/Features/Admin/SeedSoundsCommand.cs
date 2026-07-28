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
    private const string ContainerName = StorageNames.Containers.Sounds;
    private const string TableName = "SoundAssets";
    private const string PartitionKey = "library";

    public static async Task<int> RunAsync(string[] args, IConfiguration config)
    {
        // --seeds-dir override; try several candidate paths when not specified
        var cwd = Directory.GetCurrentDirectory();
        var seedsDir = GetArg(args, "--seeds-dir")
                    ?? new[]
                    {
                        Path.Combine(cwd, "tools", "meme-sounds"),
                        Path.Combine(cwd, "SCRIPTS", "meme-sounds"),
                        Path.GetFullPath(Path.Combine(cwd, "..", "..", "SCRIPTS", "meme-sounds")),
                        Path.GetFullPath(Path.Combine(cwd, "..", "..", "tools", "meme-sounds")),
                    }.FirstOrDefault(d => File.Exists(Path.Combine(d, "sounds-metadata.json")))
                    ?? Path.Combine(cwd, "tools", "meme-sounds");

        var metaFile = Path.Combine(seedsDir, "sounds-metadata.json");

        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║  PoMemeVideo // SOUND LIBRARY SEEDER             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.WriteLine();

        if (!File.Exists(metaFile))
        {
            Console.Error.WriteLine($"✗ Metadata file not found: {metaFile}");
            Console.Error.WriteLine("  Run tools/download-meme-sounds.py first, or pass --seeds-dir <path>.");
            return 1;
        }

        // ── Parse metadata ───────────────────────────────────────────────────
        var json = await File.ReadAllTextAsync(metaFile);
        var meta = JsonSerializer.Deserialize<SoundsMetadata>(json, JsonOptions)!;
        Console.WriteLine($"  Found {meta.Sounds.Count} sounds in metadata.");
        Console.WriteLine();

        // ── Connect to storage ───────────────────────────────────────────────
        var connStr = config.GetConnectionString("AzureStorage")
                   ?? config["ConnectionStrings:AzureStorage"]
                   ?? "UseDevelopmentStorage=true";

        var blobServiceClient = new BlobServiceClient(connStr);
        var tableServiceClient = new Azure.Data.Tables.TableServiceClient(connStr);

        // Ensure container exists
        var containerClient = blobServiceClient.GetBlobContainerClient(ContainerName);
        await containerClient.CreateIfNotExistsAsync();
        Console.WriteLine($"  ✓ Blob container '{ContainerName}' ready.");

        // Ensure table exists
        await tableServiceClient.CreateTableIfNotExistsAsync(TableName);
        var tableClient = tableServiceClient.GetTableClient(TableName);
        Console.WriteLine($"  ✓ Table '{TableName}' ready.");
        Console.WriteLine();

        // ── Build vocabulary for embeddings ──────────────────────────────────
        // Vocabulary is derived from all unique tags across the entire library (deterministic sort).
        var vocabulary = meta.Sounds
            .SelectMany(s => s.ActionVectorTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // ── Seed each sound ──────────────────────────────────────────────────
        int seeded = 0, skipped = 0, failed = 0;

        foreach (var entry in meta.Sounds)
        {
            var soundId = DeriveStableGuid(entry.Id ?? entry.Filename);

            // Check idempotency — skip if already seeded
            try
            {
                await tableClient.GetEntityAsync<TableEntity>(PartitionKey, soundId.ToString());
                Console.WriteLine($"  [SKIP] {entry.DisplayName}");
                skipped++;
                continue;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Not found — proceed with seed
            }

            // Upload blob if local file exists
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
            else
            {
                // Use sourceUrl as fallback blob reference for metadata-only seeds
                blobUrl = entry.SourceUrl ?? $"blob://sounds/{entry.Filename}";
                Console.WriteLine($"  [WARN] MP3 not found locally, using sourceUrl: {entry.Filename}");
            }

            // Insert table row
            var entity = new TableEntity(PartitionKey, soundId.ToString())
            {
                ["DisplayName"] = entry.DisplayName,
                ["DurationMs"] = entry.DurationMs,
                ["Tags"] = string.Join(",", entry.ActionVectorTags),
                ["BlobUrl"] = blobUrl,
                ["Priority"] = entry.Priority,
            };

            await tableClient.AddEntityAsync(entity);
            Console.WriteLine($"  [SEED] {entry.DisplayName}");
            seeded++;
        }

        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════╗");
        Console.WriteLine($"║  DONE — Seeded: {seeded,4} | Skipped: {skipped,4} | Failed: {failed,4}  ║");
        Console.WriteLine($"╚══════════════════════════════════════════════════╝");

        return failed > 0 ? 1 : 0;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
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
