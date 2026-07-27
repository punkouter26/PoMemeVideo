// SOLID: Single Responsibility — script persistence isolated
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;

namespace PoMemeVideo.Api.Features.Processing;

public sealed class DirectorScriptTableRepository : IDirectorScriptRepository
{
    private const string TableName = StorageNames.Tables.DirectorScripts;

    private readonly AzureTableClientFactory _factory;

    public DirectorScriptTableRepository(AzureTableClientFactory factory)
        => _factory = factory;

    public async Task SaveAsync(DirectorScript script, CancellationToken cancellationToken = default)
    {
        var client = _factory.GetTableClient(TableName);
        await client.CreateIfNotExistsAsync(cancellationToken);

        var entity = new TableEntity(script.SessionId.ToString(), "script")
        {
            ["GeneratedAt"] = script.GeneratedAt,
            ["TotalSoundCount"] = script.TotalSoundCount,
            ["AverageDensitySeconds"] = script.AverageDensitySeconds,
            ["EntriesJson"] = script.EntriesJson,
        };

        await client.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<DirectorScript?> GetBySessionIdAsync(
        SessionId sessionId, CancellationToken cancellationToken = default)
    {
        var client = _factory.GetTableClient(TableName);
        try
        {
            var response = await client.GetEntityAsync<TableEntity>(
                sessionId.ToString(), "script", cancellationToken: cancellationToken);
            var e = response.Value;
            return new DirectorScript
            {
                SessionId = sessionId,
                GeneratedAt = e.GetDateTimeOffset("GeneratedAt") ?? DateTimeOffset.UtcNow,
                TotalSoundCount = e.GetInt32("TotalSoundCount") ?? 0,
                AverageDensitySeconds = e.GetDouble("AverageDensitySeconds") ?? 0,
                EntriesJson = e.GetString("EntriesJson") ?? "[]",
            };
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteBySessionIdAsync(SessionId sessionId, CancellationToken cancellationToken = default)
    {
        var client = _factory.GetTableClient(TableName);
        await client.DeleteEntityAsync(sessionId.ToString(), "script", cancellationToken: cancellationToken);
    }
}

public static class DirectorScriptTableRepositoryExtensions
{
    public static IServiceCollection AddDirectorScriptTableRepository(this IServiceCollection services)
        => services.AddScoped<IDirectorScriptRepository, DirectorScriptTableRepository>();
}
