// GoF: Repository Pattern
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;

namespace PoMemeVideo.Api.Features.Auth;

internal sealed class UserIdentityTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // IdentityType ("ANON" | "Microsoft")
    public string RowKey { get; set; } = string.Empty;       // IdentityId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UserIdentityTableRepository : IUserIdentityRepository
{
    private const string TableName = StorageNames.Tables.UserIdentities;
    private readonly TableClient _table;

    public UserIdentityTableRepository(AzureTableClientFactory factory)
    {
        _table = factory.GetTableClient(TableName);
    }

    public async Task<UserIdentity> CreateAsync(UserIdentity identity, CancellationToken cancellationToken = default)
    {
        var entity = new UserIdentityTableEntity
        {
            PartitionKey = identity.IdentityType,
            RowKey = identity.IdentityId.ToString(),
            DisplayName = identity.DisplayName,
            CreatedAt = identity.CreatedAt,
        };

        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        return identity;
    }

    public async Task<UserIdentity?> GetByIdAsync(Guid identityId, string identityType, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _table.GetEntityAsync<UserIdentityTableEntity>(
                partitionKey: identityType,
                rowKey: identityId.ToString(),
                cancellationToken: cancellationToken);

            var entity = response.Value;
            return new UserIdentity
            {
                IdentityId = Guid.Parse(entity.RowKey),
                IdentityType = entity.PartitionKey,
                DisplayName = entity.DisplayName,
                CreatedAt = entity.CreatedAt,
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}

public static class UserIdentityTableRepositoryExtensions
{
    // SOLID: Dependency Inversion — callers depend on IUserIdentityRepository abstraction
    public static IServiceCollection AddUserIdentityTableRepository(this IServiceCollection services)
        => services.AddScoped<IUserIdentityRepository, UserIdentityTableRepository>();
}
