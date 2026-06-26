
namespace PoMemeVideo.Api.Features.Processing;

public interface IDirectorScriptRepository
{
    Task SaveAsync(DirectorScript script, CancellationToken cancellationToken = default);
    Task<DirectorScript?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task DeleteBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
