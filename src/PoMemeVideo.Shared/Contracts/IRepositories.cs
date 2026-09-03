// Cross-slice persistence contracts. Slices depend on these interfaces, never on a sibling
// slice's concrete repository — that is what keeps Features/<Slice> folders autonomous.
using PoMemeVideo.Shared.Domain;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Shared.Contracts;

public interface ISoundAssetRepository
{
    Task<IReadOnlyList<SoundAsset>> LoadAllAsync(CancellationToken cancellationToken = default);
    Task AddSoundAsync(SoundAsset asset, CancellationToken cancellationToken = default);

    /// <summary>Evicts the in-memory cache so the next LoadAllAsync re-reads from storage.</summary>
    void InvalidateCache();
}

public interface IVideoSessionRepository
{
    Task<VideoSession> CreateAsync(VideoSession session, CancellationToken cancellationToken = default);
    Task<VideoSession?> GetByIdAsync(SessionId sessionId, UserId userId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Lists the caller's <c>Complete</c> AND <c>Error</c> sessions, newest first. Failed
    /// sessions are intentionally surfaced so the user can find and retry them from the
    /// History page rather than re-running from scratch.
    /// </summary>
    Task<IReadOnlyList<VideoSession>> ListCompletedAsync(UserId userId, CancellationToken cancellationToken = default);
    Task UpdateMetadataAsync(SessionId sessionId, UserId userId, string sourceBlobPath, double videoDurationSeconds, bool aggressiveVisuals, double? trimStartSeconds = null, double? trimDurationSeconds = null, string? memePersona = null, string? aspectRatio = null, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(SessionId sessionId, UserId userId, SessionStatus status, string? errorMessage = null, string? outputBlobPath = null, double? videoDurationSeconds = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(SessionId sessionId, UserId userId, CancellationToken cancellationToken = default);
}

public interface IDirectorScriptRepository
{
    Task SaveAsync(DirectorScript script, CancellationToken cancellationToken = default);
    Task<DirectorScript?> GetBySessionIdAsync(SessionId sessionId, CancellationToken cancellationToken = default);
    Task DeleteBySessionIdAsync(SessionId sessionId, CancellationToken cancellationToken = default);
}
