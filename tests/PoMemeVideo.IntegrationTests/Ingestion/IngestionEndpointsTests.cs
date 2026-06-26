using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using PoMemeVideo.Api.Ingestion;
using PoMemeVideo.Api.Entities;
using PoMemeVideo.Api.Interfaces;
using PoMemeVideo.Api.AzureStorage;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.IntegrationTests.Ingestion;

/// <summary>
/// Integration tests for POST /api/ingestion/sas and POST /api/ingestion/sessions.
/// Uses NSubstitute mocks to avoid real Azure dependency while still exercising
/// the full ASP.NET Core middleware and routing pipeline.
/// </summary>
[Collection("Integration")]
public sealed class IngestionEndpointsTests : IAsyncLifetime
{
    private readonly IVideoSessionRepository _repository = Substitute.For<IVideoSessionRepository>();
    // BlobServiceClientFactory.GenerateUploadSasUriAsync is virtual; ForPartsOf lets us intercept
    // that single method while the rest of the class behaves normally.  The constructor succeeds
    // with "UseDevelopmentStorage=true" because BlobServiceClient is created lazily-on-use only.
    private readonly BlobServiceClientFactory _blobFactory;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public IngestionEndpointsTests()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AzureBlobStorage"] = "UseDevelopmentStorage=true",
            })
            .Build();

        _blobFactory = Substitute.ForPartsOf<BlobServiceClientFactory>(cfg);
        _blobFactory
            .GenerateUploadSasUriAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new Uri("https://fake.blob.core.windows.net/source/video.mp4?sv=2025-07-05&sig=faketoken"));
    }

    public Task InitializeAsync()
    {
        // Arrange: repository returns the session it receives (identity)
        _repository
            .CreateAsync(Arg.Any<VideoSession>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.ArgAt<VideoSession>(0)));

        _repository
            .UpdateMetadataAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<double>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.UseSetting("KeyVault:Uri", ""); // skip KV in CI/test

                builder.ConfigureServices(services =>
                {
                    // Replace the real repository with our mock
                    services.RemoveAll<IVideoSessionRepository>();
                    services.AddScoped<IVideoSessionRepository>(_ => _repository);

                    // Replace IngestVideoCommand so it uses our mock repository
                    services.RemoveAll<IngestVideoCommand>();
                    services.AddScoped(_ => new IngestVideoCommand(_repository));

                    // Replace BlobServiceClientFactory with our partial mock.
                    // GenerateUploadSasUriAsync is mocked to return a fake URL,
                    // so no real Azurite or Azure storage is required.
                    services.RemoveAll<BlobServiceClientFactory>();
                    services.AddSingleton(_ => _blobFactory);
                });
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
    }

    // ── POST /api/ingestion/sas ──────────────────────────────────────────

    [Fact]
    public async Task PostSas_ValidMp4_Returns200WithSessionIdAndSasUrl()
    {
        var response = await _client!.PostAsJsonAsync("/api/ingestion/sas", new
        {
            fileName = "test-video.mp4",
            fileSizeBytes = 10_485_760L, // 10 MB
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SasResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.SessionId);
        // SAS URL may be a mock or real; just assert it's not empty
        Assert.False(string.IsNullOrWhiteSpace(body.SasUrl));
    }

    [Theory]
    [InlineData(".exe", 1024L)]
    [InlineData(".mkv", 1024L)]
    public async Task PostSas_InvalidExtension_Returns400WithErrorCode(string extension, long size)
    {
        var response = await _client!.PostAsJsonAsync("/api/ingestion/sas", new
        {
            fileName = $"video{extension}",
            fileSizeBytes = size,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("INVALID_EXTENSION", body?.Error);
    }

    [Fact]
    public async Task PostSas_FileTooLarge_Returns400WithFileTooLargeCode()
    {
        const long over500Mb = 501L * 1024 * 1024;

        var response = await _client!.PostAsJsonAsync("/api/ingestion/sas", new
        {
            fileName = "huge.mp4",
            fileSizeBytes = over500Mb,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("FILE_TOO_LARGE", body?.Error);
    }

    // ── POST /api/ingestion/sessions ────────────────────────────────────

    [Fact]
    public async Task PostSessions_ValidSession_Returns201()
    {
        var sessionId = Guid.NewGuid();

        // Arrange: the repository reports the session as existing
        _repository
            .GetByIdAsync(sessionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<VideoSession?>(new VideoSession
            {
                SessionId = sessionId,
                UserId = Guid.Empty,
                SourceBlobPath = $"sessions/{sessionId}/source.mp4",
            }));

        var response = await _client!.PostAsJsonAsync("/api/ingestion/sessions", new
        {
            sessionId,
            blobPath = $"sessions/{sessionId}/source.mp4",
            videoDurationSeconds = 42.0,
            aggressiveVisuals = true,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.Equal(sessionId, body?.SessionId);
    }

    [Fact]
    public async Task PostSessions_UnknownSession_Returns404()
    {
        var sessionId = Guid.NewGuid();

        _repository
            .GetByIdAsync(sessionId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<VideoSession?>(null));

        var response = await _client!.PostAsJsonAsync("/api/ingestion/sessions", new
        {
            sessionId,
            blobPath = $"sessions/{sessionId}/source.mp4",
            videoDurationSeconds = 42.0,
            aggressiveVisuals = false,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Response model helpers ───────────────────────────────────────────

    private sealed record SasResponse(Guid SessionId, string SasUrl, DateTimeOffset ExpiresAt);
    private sealed record ErrorResponse(string Error, string Message);
    private sealed record SessionResponse(Guid SessionId, string Status);
}
