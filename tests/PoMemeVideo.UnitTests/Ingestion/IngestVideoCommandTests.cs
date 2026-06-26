using NSubstitute;

namespace PoMemeVideo.UnitTests.Ingestion;

public sealed class IngestVideoCommandTests
{
    private readonly IVideoSessionRepository _repository = Substitute.For<IVideoSessionRepository>();
    private readonly IngestVideoCommand _command;

    public IngestVideoCommandTests()
    {
        _command = new IngestVideoCommand(_repository);

        // Default: repository returns the session as-is
        _repository
            .CreateAsync(Arg.Any<VideoSession>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.ArgAt<VideoSession>(0)));
    }

    // ── Extension validation ──────────────────────────────────────────────

    [Theory]
    [InlineData(".mp4")]
    [InlineData(".MP4")]
    [InlineData(".mov")]
    [InlineData(".avi")]
    [InlineData(".webm")]
    public async Task ExecuteAsync_ValidExtension_Succeeds(string extension)
    {
        var result = await _command.ExecuteAsync(
            $"video{extension}",
            fileSizeBytes: 1024,
            userId: Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Contains(extension.ToLowerInvariant(), result.SourceBlobPath);
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".mkv")]
    [InlineData(".flv")]
    [InlineData(".txt")]
    [InlineData("")]
    public async Task ExecuteAsync_InvalidExtension_ThrowsWithInvalidExtensionCode(string extension)
    {
        var ex = await Assert.ThrowsAsync<VideoIngestionValidationException>(() =>
            _command.ExecuteAsync(
                $"video{extension}",
                fileSizeBytes: 1024,
                userId: Guid.NewGuid()));

        Assert.Equal("INVALID_EXTENSION", ex.ErrorCode);
    }

    // ── File size validation ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ExactlyMaxSize_Succeeds()
    {
        var result = await _command.ExecuteAsync(
            "video.mp4",
            fileSizeBytes: IngestVideoCommand.MaxFileSizeBytes,
            userId: Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, result.SessionId);
    }

    [Fact]
    public async Task ExecuteAsync_OneByteUnderMax_Succeeds()
    {
        var result = await _command.ExecuteAsync(
            "video.mp4",
            fileSizeBytes: IngestVideoCommand.MaxFileSizeBytes - 1,
            userId: Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, result.SessionId);
    }

    [Fact]
    public async Task ExecuteAsync_501MB_ThrowsFileTooLarge()
    {
        const long size501Mb = 501L * 1024 * 1024;

        var ex = await Assert.ThrowsAsync<VideoIngestionValidationException>(() =>
            _command.ExecuteAsync("video.mp4", size501Mb, userId: Guid.NewGuid()));

        Assert.Equal("FILE_TOO_LARGE", ex.ErrorCode);
        Assert.Equal(IngestVideoCommand.MaxFileSizeBytes, ex.MaxBytes);
        Assert.Equal(size501Mb, ex.ReceivedBytes);
    }

    [Fact]
    public async Task ExecuteAsync_499MB_Succeeds()
    {
        var result = await _command.ExecuteAsync(
            "video.mp4",
            fileSizeBytes: 499L * 1024 * 1024,
            userId: Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, result.SessionId);
    }

    // ── Session creation ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidInput_PersistsSession()
    {
        var userId = Guid.NewGuid();

        await _command.ExecuteAsync("video.mp4", 1024, userId);

        await _repository.Received(1).CreateAsync(
            Arg.Is<VideoSession>(s => s.UserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ValidInput_BlobPathContainsSessionId()
    {
        var result = await _command.ExecuteAsync("video.mp4", 1024, Guid.NewGuid());

        Assert.Contains(result.SessionId.ToString(), result.SourceBlobPath);
    }

    [Fact]
    public async Task ExecuteAsync_ValidInput_BlobPathStartsWithSessions()
    {
        var result = await _command.ExecuteAsync("my-video.mp4", 1024, Guid.NewGuid());

        Assert.StartsWith("sessions/", result.SourceBlobPath);
        Assert.EndsWith(".mp4", result.SourceBlobPath);
    }
}
