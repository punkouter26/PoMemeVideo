using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using PoMemeVideo.Api.Features.Processing;

namespace PoMemeVideo.Api.Features.Admin;

public static class AdminEndpoints
{
    private const string SoundContainer = "sounds";
    private const string SoundTable = "SoundAssets";
    private const string SoundPartition = "library";

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // DELETE /api/admin/data — wipe ALL session blobs + VideoSessions + DirectorScripts tables.
        // Sound data (sounds container + SoundAssets table) is preserved.
        app.MapDelete("/api/admin/data", async (
            BlobStorageService blobs,
            AzureTableClientFactory tableFactory,
            ISoundAssetRepository soundRepo,
            CancellationToken ct) =>
        {
            // 1. Delete every blob in the sessions container
            await blobs.DeleteBlobsByPrefixAsync("sessions/", ct);

            // 2. Delete all rows from VideoSessions table
            await ClearTableAsync(tableFactory.GetTableClient("VideoSessions"), ct);

            // 3. Delete all rows from DirectorScripts table
            await ClearTableAsync(tableFactory.GetTableClient("DirectorScripts"), ct);

            // 4. Invalidate the sound cache so any subsequent re-seed is visible immediately
            soundRepo.InvalidateCache();

            return Results.Ok(new { cleared = true, message = "All session data wiped. Sound library intact." });
        })
        .WithName("ClearAllData")
        .WithTags("Admin")
        .Produces<object>(200)
        .RequireAuthorization();

        // POST /api/admin/sounds/invalidate-cache — evict the in-memory sound library cache.
        // Useful after seeding from an external tool without restarting the API.
        app.MapPost("/api/admin/sounds/invalidate-cache", (ISoundAssetRepository soundRepo) =>
        {
            soundRepo.InvalidateCache();
            return Results.Ok(new { invalidated = true, message = "Sound library cache evicted. Next load will re-read from storage." });
        })
        .WithName("InvalidateSoundCache")
        .WithTags("Admin")
        .Produces<object>(200)
        .AllowAnonymous();

        // ── POST /api/admin/sounds/seed — production-safe HTTP seeding ───────
        // Accepts the sounds-metadata.json body (same format as CLI).
        // Idempotent: skips rows that already exist in Table Storage.
        // Works against whichever storage is configured (Azurite local / Azure prod).
        //
        // Usage from dev machine to seed production:
        //   curl -X POST https://myapp.azurewebsites.net/api/admin/sounds/seed \
        //     -H "Content-Type: application/json" \
        //     -H "Cookie: .AspNetCore.Cookies=<auth-cookie>" \
        //     -d @SCRIPTS/meme-sounds/sounds-metadata.json
        app.MapPost("/api/admin/sounds/seed", async (
            HttpRequest request,
            AzureTableClientFactory tableFactory,
            BlobServiceClientFactory blobFactory,
            ISoundAssetRepository soundRepo,
            IHttpClientFactory httpClientFactory,
            CancellationToken ct) =>
        {
            // Parse metadata from request body
            SoundsMetadata meta;
            try
            {
                meta = await request.ReadFromJsonAsync<SoundsMetadata>(
                    SeedSoundsJsonOptions, ct) ?? new SoundsMetadata();
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new { error = "Invalid JSON body.", detail = ex.Message });
            }

            if (meta.Sounds is null || meta.Sounds.Count == 0)
                return Results.BadRequest(new { error = "No sounds found in payload. Expected { sounds: [...] }" });

            // Connect to storage via configured DI factories
            var blobContainer = blobFactory.GetContainerClient(SoundContainer);

            var tableClient = tableFactory.GetTableClient(SoundTable);
            await tableClient.CreateIfNotExistsAsync(ct);

            // Build vocabulary from all unique tags (matching CLI behaviour)
            var vocabulary = meta.Sounds
                .SelectMany(s => s.ActionVectorTags ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            int seeded = 0, skipped = 0, failed = 0;

            foreach (var entry in meta.Sounds)
            {
                var soundId = DeriveStableGuid(entry.Id ?? entry.Filename);

                // Self-healing idempotency: skip only when BOTH the table row AND its
                // backing blob already exist. A row whose blob is missing (e.g. seeded
                // earlier with an external sourceUrl and never uploaded) is re-healed.
                var rowExists = await TableRowExistsAsync(tableClient, soundId, ct);
                var blobExists = !string.IsNullOrWhiteSpace(entry.Filename)
                    && await blobContainer.GetBlobClient(entry.Filename).ExistsAsync(ct);
                if (rowExists && blobExists)
                {
                    skipped++;
                    continue;
                }

                // Self-host the asset: download from the source URL and upload to our
                // blob container, then store OUR blob URL. The render and stream
                // pipelines read sounds from blob storage, so an external sourceUrl
                // alone would leave them unplayable (BlobNotFound).
                var blobUrl = await DownloadAndStoreSoundAsync(
                    http, blobContainer, entry, ct);
                if (blobUrl is null)
                {
                    failed++;
                    continue;
                }

                // Compute embedding vector
                var embedding = new ActionVector(entry.ActionVectorTags ?? [])
                    .ToEmbedding(vocabulary);
                var embeddingCsv = string.Join(",",
                    embedding.Select(f => f.ToString("G",
                        System.Globalization.CultureInfo.InvariantCulture)));

                var entity = new TableEntity(SoundPartition, soundId.ToString())
                {
                    ["DisplayName"] = entry.DisplayName,
                    ["DurationMs"] = entry.DurationMs,
                    ["Tags"] = string.Join(",", entry.ActionVectorTags ?? []),
                    ["BlobUrl"] = blobUrl,
                    ["EmbeddingVector"] = embeddingCsv,
                };

                try
                {
                    await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
                    seeded++;
                }
                catch
                {
                    failed++;
                }
            }

            // Invalidate cache so next load picks up newly seeded rows
            soundRepo.InvalidateCache();

            return Results.Ok(new
            {
                seeded,
                skipped,
                failed,
                total = meta.Sounds.Count,
                message = $"Seeding complete. {seeded} new, {skipped} skipped, {failed} failed. Cache invalidated.",
            });
        })
        .WithName("SeedSounds")
        .WithTags("Admin")
        .Produces<object>(200)
        .Produces<object>(400)
        .RequireAuthorization();

        return app;
    }

    private static async Task<bool> TableRowExistsAsync(TableClient table, Guid soundId, CancellationToken ct)
    {
        try
        {
            await table.GetEntityAsync<TableEntity>(SoundPartition, soundId.ToString(), cancellationToken: ct);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads a sound from its source URL and uploads it to the "sounds" blob
    /// container under its filename. Returns the stored blob's URL, or null if the
    /// download/upload failed (caller counts it as a failure and skips the row).
    /// </summary>
    private static async Task<string?> DownloadAndStoreSoundAsync(
        HttpClient http,
        BlobContainerClient container,
        SoundEntry entry,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.SourceUrl) || string.IsNullOrWhiteSpace(entry.Filename))
            return null;

        var blobClient = container.GetBlobClient(entry.Filename);

        // Already uploaded on a prior run — reuse it (idempotent).
        if (await blobClient.ExistsAsync(ct))
            return blobClient.Uri.ToString();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, entry.SourceUrl);
            // myinstants.com (and similar) reject requests without a browser User-Agent.
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (PoMemeVideo sound seeder)");

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var sourceStream = await resp.Content.ReadAsStreamAsync(ct);
            await blobClient.UploadAsync(
                sourceStream,
                new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "audio/mpeg" } },
                ct);

            return blobClient.Uri.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static async Task ClearTableAsync(TableClient table, CancellationToken ct)
    {
        // Table may not exist yet — ignore not-found errors
        try
        {
            await foreach (var entity in table.QueryAsync<TableEntity>(cancellationToken: ct))
            {
                await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: ct);
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Table doesn't exist yet — nothing to clear
        }
    }

    // ── Seed helpers (shared with CLI SeedSoundsCommand) ─────────────────────

    private static Guid DeriveStableGuid(string slug)
    {
        var namespaceBytes = new byte[] { 0x6b, 0xa7, 0xb8, 0x10, 0x9d, 0xad, 0x11, 0xd1, 0x80, 0xb4, 0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8 };
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(slug);
        var combined = namespaceBytes.Concat(nameBytes).ToArray();
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(combined);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16]);
    }

    private static readonly JsonSerializerOptions SeedSoundsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
    }
}
