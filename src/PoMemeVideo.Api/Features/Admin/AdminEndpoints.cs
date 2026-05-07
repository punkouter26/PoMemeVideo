using Azure.Data.Tables;
using PoMemeVideo.Infrastructure.AzureStorage;

namespace PoMemeVideo.Api.Features.Admin;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // DELETE /api/admin/data — wipe ALL session blobs + VideoSessions + DirectorScripts tables.
        // Sound data (sounds container + SoundAssets table) is preserved.
        app.MapDelete("/api/admin/data", async (
            BlobStorageService blobs,
            AzureTableClientFactory tableFactory,
            CancellationToken ct) =>
        {
            // 1. Delete every blob in the sessions container
            await blobs.DeleteBlobsByPrefixAsync("sessions/", ct);

            // 2. Delete all rows from VideoSessions table
            await ClearTableAsync(tableFactory.GetTableClient("VideoSessions"), ct);

            // 3. Delete all rows from DirectorScripts table
            await ClearTableAsync(tableFactory.GetTableClient("DirectorScripts"), ct);

            return Results.Ok(new { cleared = true, message = "All session data wiped. Sound library intact." });
        })
        .WithName("ClearAllData")
        .WithTags("Admin")
        .Produces<object>(200)
        .AllowAnonymous();

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
