// SOLID: Single Responsibility — all Table Storage client creation isolated here
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PoMemeVideo.Api.AzureStorage;

public class AzureTableClientFactory
{
    private readonly TableServiceClient _client;

    public AzureTableClientFactory(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AzureTableStorage");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _client = new TableServiceClient(connectionString);
        }
        else
        {
            var endpoint = configuration["Azure:TableStorage:Endpoint"]
                ?? throw new InvalidOperationException("Azure Table Storage endpoint not configured.");
            _client = new TableServiceClient(new Uri(endpoint), new DefaultAzureCredential());
        }
    }

    public TableClient GetTableClient(string tableName)
    {
        var client = _client.GetTableClient(tableName);
        client.CreateIfNotExists();
        return client;
    }
}

public static class AzureTableClientFactoryExtensions
{
    public static IServiceCollection AddAzureTableClientFactory(this IServiceCollection services)
        => services.AddSingleton<AzureTableClientFactory>();
}
