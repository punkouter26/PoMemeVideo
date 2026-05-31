// GoF: Repository Pattern — sound library query endpoint
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure.AzureStorage;
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
            int limit = 20,
            int offset = 0,
            CancellationToken cancellationToken = default) =>
        {
            var allSounds = await repository.LoadAllAsync(cancellationToken);

            var filtered = string.IsNullOrWhiteSpace(tags)
                ? allSounds
                : allSounds.Where(s =>
                {
                    var requestedTags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return requestedTags.Any(t => s.ActionVectorTags.Contains(t, StringComparer.OrdinalIgnoreCase));
                }).ToList();

            var totalCount = filtered.Count;
            var page = filtered.Skip(Math.Max(0, offset)).Take(Math.Min(limit, 100)).Select(s => new SoundAssetDto
            {
                SoundId = s.SoundId,
                DisplayName = s.DisplayName,
                DurationMs = s.DurationMs,
                ActionVectorTags = s.ActionVectorTags,
                BlobUrl = s.BlobUrl,
            }).ToArray();

            return Results.Ok(new { totalCount, sounds = page });
        });

        // GET /api/memelibrary/sounds/{soundId}/stream — proxy sound file from blob storage to browser
        group.MapGet("/sounds/{soundId:guid}/stream", async (
            Guid soundId,
            ISoundAssetRepository repository,
            BlobStorageService blobService,
            CancellationToken cancellationToken) =>
        {
            var allSounds = await repository.LoadAllAsync(cancellationToken);
            var sound = allSounds.FirstOrDefault(s => s.SoundId == soundId);
            if (sound is null)
                return Results.NotFound();

            // Extract relative blob path from the full Azurite URL
            var blobPath = ExtractBlobPath(sound.BlobUrl);
            try
            {
                var stream = await blobService.StreamBlobAsync(blobPath, cancellationToken);
                return Results.File(stream, contentType: "audio/mpeg", enableRangeProcessing: true);
            }
            catch
            {
                return Results.NotFound();
            }
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
