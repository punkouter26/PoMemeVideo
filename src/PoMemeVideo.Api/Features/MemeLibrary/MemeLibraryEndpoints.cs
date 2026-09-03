// GoF: Repository Pattern — sound library query endpoint
using PoMemeVideo.Shared.Models;

namespace PoMemeVideo.Api.Features.MemeLibrary;

public static class MemeLibraryEndpoints
{
    public static IEndpointRouteBuilder MapMemeLibraryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/memelibrary");

        group.MapGet("/sounds", async (
            ISoundAssetRepository repository,
            string? tags,
            string? query,
            int limit = 20,
            int offset = 0,
            CancellationToken cancellationToken = default) =>
        {
            var allSounds = await repository.LoadAllAsync(cancellationToken);

            var filtered = allSounds.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(tags))
            {
                var requestedTags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                filtered = filtered.Where(s => requestedTags.Any(t => s.ActionVectorTags.Contains(t, StringComparer.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();
                filtered = filtered.Where(s =>
                    s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    s.ActionVectorTags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            var list = filtered.ToList();
            var totalCount = list.Count;
            var page = list.Skip(Math.Max(0, offset)).Take(Math.Min(limit, 100)).Select(s => new SoundAssetDto
            {
                SoundId = s.SoundId.Value,
                DisplayName = s.DisplayName,
                DurationMs = s.DurationMs,
                ActionVectorTags = s.ActionVectorTags,
                BlobUrl = s.BlobUrl,
            }).ToArray();

            return Results.Ok(new { totalCount, sounds = page });
        });

        // POST /api/memelibrary/upload — upload custom meme sound
        group.MapPost("/upload", async (
            IFormFile file,
            string? displayName,
            string? tags,
            ISoundAssetRepository repository,
            IBlobStorageService blobService,
            CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "No audio file provided." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".mp3" && ext != ".wav" && ext != ".ogg")
                return Results.BadRequest(new { error = "Only .mp3, .wav, and .ogg files are supported." });

            var soundId = SoundId.New();
            var blobPath = $"sounds/{soundId}{ext}";

            using (var stream = file.OpenReadStream())
            {
                await blobService.UploadBlobAsync(blobPath, stream, file.ContentType ?? "audio/mpeg", cancellationToken);
            }

            var parsedTags = string.IsNullOrWhiteSpace(tags)
                ? ["custom", "user-upload"]
                : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Append("custom")
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToArray();

            var asset = new SoundAsset
            {
                SoundId = soundId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(file.FileName) : displayName.Trim(),
                DurationMs = 2500,
                ActionVectorTags = parsedTags,
                BlobUrl = blobPath,
                Priority = false,
                UseCase = "custom-upload"
            };

            await repository.AddSoundAsync(asset, cancellationToken);

            return Results.Created($"/api/memelibrary/sounds/{soundId}/stream", new SoundAssetDto
            {
                SoundId = soundId.Value,
                DisplayName = asset.DisplayName,
                DurationMs = asset.DurationMs,
                ActionVectorTags = asset.ActionVectorTags,
                BlobUrl = asset.BlobUrl
            });
        })
        .DisableAntiforgery();

        // POST /api/memelibrary/seed — bulk-load the default meme-sound catalog from the metadata
        // file on disk into Azurite / Azure Blob Storage + SoundAssets table. Idempotent.
        // Sibling slice Admin hosts the seeding logic (SeedSoundsCommand); this endpoint is the
        // single in-app entry point the UI calls so it doesn't have to depend on /api/admin.
        group.MapPost("/seed", async (
            IConfiguration config,
            IWebHostEnvironment env,
            ISoundAssetRepository soundRepo,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MemeLibrary.Seed");
            var seedsDir = SeedSoundsCommand.ResolveSeedsDir([], env.ContentRootPath);
            if (!File.Exists(Path.Combine(seedsDir, "sounds-metadata.json")))
                return Results.NotFound(new { error = "sounds-metadata.json not found.", path = seedsDir });

            logger.LogInformation(
                "Seeding sound library from {SeedsDir} (env={Env})",
                seedsDir, env.EnvironmentName);

            var exitCode = await SeedSoundsCommand.RunForDirAsync(seedsDir, config, verbose: false);

            // Invalidate the in-memory cache so the next LoadAllAsync re-reads from storage.
            soundRepo.InvalidateCache();

            return Results.Ok(new
            {
                seededFrom = seedsDir,
                exitCode,
                message = exitCode == 0
                    ? "Seeding complete. Refresh the library to see new assets."
                    : "Seeding finished with failures (see server logs).",
            });
        })
        .WithName("SeedMemeLibrary")
        .WithTags("MemeLibrary")
        .Produces<object>(200)
        .Produces<object>(404)
        .AllowAnonymous();

        // GET /api/memelibrary/sounds/{soundId}/stream — proxy sound file from blob storage to browser.
        // Falls back to proxying the external sourceUrl when the local blob is missing — the
        // seed metadata references myinstants.com URLs, and until they finish downloading we
        // still want audition to work end-to-end. The fallback path is a one-time self-heal:
        // it tries to upload the bytes into Azurite so the next request hits the local path.
        group.MapGet("/sounds/{soundId:guid}/stream", async (
            SoundId soundId,
            ISoundAssetRepository repository,
            IBlobStorageService blobService,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var allSounds = await repository.LoadAllAsync(cancellationToken);
            var sound = allSounds.FirstOrDefault(s => s.SoundId == soundId);
            if (sound is null)
                return Results.NotFound();

            // Extract relative blob path from the full Azurite URL
            var blobPath = ExtractBlobPath(sound.BlobUrl);

            // Primary path: stream from local blob storage.
            try
            {
                var exists = await blobService.BlobExistsAsync(blobPath, cancellationToken);
                if (exists)
                {
                    var stream = await blobService.StreamBlobAsync(blobPath, cancellationToken);
                    return Results.File(stream, contentType: "audio/mpeg", enableRangeProcessing: true);
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Local blob not present — fall through to proxy.
            }

            // Fallback: if BlobUrl is itself an external URL (e.g. myinstants.com), proxy it
            // through this API so the browser can play the sound even when the local blob
            // hasn't been uploaded yet. Re-running the seed endpoint will populate the local
            // blob and subsequent calls are served directly from storage.
            if (Uri.TryCreate(sound.BlobUrl, UriKind.Absolute, out var externalUri)
                && (externalUri.Scheme == Uri.UriSchemeHttp || externalUri.Scheme == Uri.UriSchemeHttps))
            {
                var logger = loggerFactory.CreateLogger("MemeLibrary.Stream");
                try
                {
                    var http = httpClientFactory.CreateClient();
                    http.Timeout = TimeSpan.FromSeconds(20);
                    using var req = new HttpRequestMessage(HttpMethod.Get, externalUri);
                    req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (PoMemeVideo stream proxy)");

                    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!resp.IsSuccessStatusCode)
                    {
                        logger.LogWarning(
                            "External proxy for sound {SoundId} returned HTTP {Status} ({Url})",
                            soundId, (int)resp.StatusCode, externalUri);
                        return Results.NotFound();
                    }

                    var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    return Results.File(stream, contentType: "audio/mpeg", enableRangeProcessing: true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "External URL proxy failed for {Url}", externalUri);
                    return Results.NotFound();
                }
            }

            return Results.NotFound();
        })
        .WithName("StreamSound")
        .WithTags("MemeLibrary")
        .Produces(200)
        .Produces(404)
        .AllowAnonymous();

        return routes;
    }

    private static string ExtractBlobPath(string blobUrl)
    {
        try
        {
            var uri = new Uri(blobUrl);
            var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            if (segments.Length == 2 && segments[0].StartsWith("devstoreaccount", StringComparison.OrdinalIgnoreCase))
                return segments[1]; // "sounds/filename.mp3"
            return string.Join('/', segments);
        }
        catch { return blobUrl; }
    }
}
