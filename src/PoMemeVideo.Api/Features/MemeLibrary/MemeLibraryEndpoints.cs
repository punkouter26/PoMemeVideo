// GoF: Repository Pattern — sound library query endpoint
using PoMemeVideo.Domain.Interfaces;
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
            var page = filtered.Take(Math.Min(limit, 100)).Select(s => new SoundAssetDto
            {
                SoundId = s.SoundId,
                DisplayName = s.DisplayName,
                DurationMs = s.DurationMs,
                ActionVectorTags = s.ActionVectorTags,
                BlobUrl = s.BlobUrl,
            }).ToArray();

            return Results.Ok(new { totalCount, sounds = page });
        });

        return routes;
    }
}
