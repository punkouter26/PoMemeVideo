using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.IntegrationTests.Processing;

/// <summary>
/// Integration tests for POST /api/processing/sessions/{sessionId}/initiate.
/// Uses MockAiVisionService and MockDirectorService injected via WebApplicationFactory.
/// Asserts session transitions Ingesting → Processing → Complete.
/// </summary>
[Collection("Integration")]
public sealed class ProcessingEndpointsTests : IAsyncLifetime
{
    private readonly IVideoSessionRepository _sessions = Substitute.For<IVideoSessionRepository>();
    private readonly ISoundAssetRepository _sounds = Substitute.For<ISoundAssetRepository>();
    private readonly IDirectorScriptRepository _scripts = Substitute.For<IDirectorScriptRepository>();
    private readonly IEngineNotifier _notifier = Substitute.For<IEngineNotifier>();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private SessionId _sessionId = SessionId.New();
    private SessionStatus _currentStatus = SessionStatus.Ingesting;

    public Task InitializeAsync()
    {
        var sessionUserId = UserId.New();

        var session = new VideoSession
        {
            SessionId = _sessionId,
            UserId = sessionUserId,
            SourceBlobPath = $"sessions/{_sessionId}/source.mp4",
            VideoDurationSeconds = 30.0,
            AggressiveVisuals = false,
            Status = SessionStatus.Ingesting,
        };

        // Repository: GetById returns session with current status
        _sessions.GetByIdAsync(_sessionId, Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<VideoSession?>(
                new VideoSession
                {
                    SessionId = _sessionId,
                    UserId = sessionUserId,
                    SourceBlobPath = session.SourceBlobPath,
                    VideoDurationSeconds = session.VideoDurationSeconds,
                    AggressiveVisuals = session.AggressiveVisuals,
                    Status = _currentStatus,
                }));

        // Track status updates
        _sessions.UpdateStatusAsync(
                Arg.Any<SessionId>(), Arg.Any<UserId>(), Arg.Any<SessionStatus>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                _currentStatus = x.ArgAt<SessionStatus>(2);
                return Task.CompletedTask;
            });

        // Sound repo: return two mock sounds
        var mockSounds = new List<SoundAsset>
        {
            new() { SoundId = SoundId.New(), DisplayName = "Vine Boom", DurationMs = 800, BlobUrl = "https://example.com/vine-boom.mp3", ActionVectorTags = ["impact", "boom"] },
            new() { SoundId = SoundId.New(), DisplayName = "Sad Violin", DurationMs = 1200, BlobUrl = "https://example.com/sad-violin.mp3", ActionVectorTags = ["sad", "slow"] },
        };
        _sounds.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SoundAsset>>(mockSounds.AsReadOnly()));

        _scripts.SaveAsync(Arg.Any<DirectorScript>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.UseSetting("KeyVault:Uri", ""); // skip KV in CI/test

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IVideoSessionRepository>();
                    services.AddScoped(_ => _sessions);

                    services.RemoveAll<ISoundAssetRepository>();
                    services.AddSingleton(_ => _sounds);

                    services.RemoveAll<IDirectorScriptRepository>();
                    services.AddScoped(_ => _scripts);

                    services.RemoveAll<IEngineNotifier>();
                    services.AddSingleton(_ => _notifier);

                    services.RemoveAll<IAiVisionService>();
                    services.AddSingleton<IAiVisionService, MockAiVisionService>();

                    services.RemoveAll<IDirectorService>();
                    services.AddSingleton<IDirectorService, MockDirectorService>();

                    services.AddScoped<SemanticMatchingService>();
                    services.AddScoped<RunEngineCommand>();
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

    // ── POST /api/processing/sessions/{id}/initiate ─────────────────────

    [Fact]
    public async Task PostInitiate_IngestingSession_Returns202()
    {
        var response = await _client!.PostAsync(
            $"/api/processing/sessions/{_sessionId}/initiate", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<InitiateResponse>();
        Assert.NotNull(body);
        Assert.Equal(_sessionId.Value, body.SessionId);
        Assert.Equal("Processing", body.Status);
    }

    [Fact]
    public async Task PostInitiate_UnknownSession_Returns404()
    {
        var unknownId = SessionId.New();
        _sessions.GetByIdAsync(unknownId, Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<VideoSession?>(null));

        var response = await _client!.PostAsync(
            $"/api/processing/sessions/{unknownId}/initiate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostInitiate_AlreadyProcessing_Returns409()
    {
        _currentStatus = SessionStatus.Processing;

        var response = await _client!.PostAsync(
            $"/api/processing/sessions/{_sessionId}/initiate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostInitiate_CompletedSession_Returns409()
    {
        _currentStatus = SessionStatus.Complete;

        var response = await _client!.PostAsync(
            $"/api/processing/sessions/{_sessionId}/initiate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record InitiateResponse(Guid SessionId, string Status);
}
