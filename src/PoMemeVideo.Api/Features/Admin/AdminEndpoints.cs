using Azure.Data.Tables;

namespace PoMemeVideo.Api.Features.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // DELETE /api/admin/data — wipe ALL session blobs + VideoSessions + DirectorScripts tables.
        // Sound data (sounds container + SoundAssets table) is preserved.
        app.MapDelete("/api/admin/data", async (
            IBlobStorageService blobs,
            AzureTableClientFactory tableFactory,
            ISoundAssetRepository soundRepo,
            CancellationToken ct) =>
        {
            // 1. Delete every blob in the sessions container
            await blobs.DeleteBlobsByPrefixAsync("sessions/", ct);

            // 2. Delete all rows from VideoSessions table
            await ClearTableAsync(tableFactory.GetTableClient(StorageNames.Tables.VideoSessions), ct);

            // 3. Delete all rows from DirectorScripts table
            await ClearTableAsync(tableFactory.GetTableClient(StorageNames.Tables.DirectorScripts), ct);

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
        // Was AllowAnonymous — an unauthenticated caller could evict the cache at will.
        // Matches the other two /api/admin endpoints.
        .RequireAuthorization();

        // Sound seeding is exposed via POST /api/memelibrary/seed (delegates to SeedSoundsCommand).
        // The old admin endpoint that took the JSON body in-process duplicated the same logic
        // with a different transport — keeping one source of truth.

        return app;
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
}
