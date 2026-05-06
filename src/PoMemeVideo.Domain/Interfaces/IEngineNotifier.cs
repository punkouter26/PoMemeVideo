// SOLID: Dependency Inversion — Application layer depends on IEngineNotifier,
// never on IHubContext<EngineHub>
using PoMemeVideo.Shared.Models;

namespace PoMemeVideo.Domain.Interfaces;

public interface IEngineNotifier
{
    Task DirectorLogAsync(Guid sessionId, string message, CancellationToken cancellationToken = default);
    Task DirectorScriptAsync(Guid sessionId, ScriptEntryDto entry, CancellationToken cancellationToken = default);
    Task AuditAsync(Guid sessionId, string message, CancellationToken cancellationToken = default);
    Task HardwareMetricsAsync(Guid sessionId, double inferenceLatencyMs, double cpuLoadPercent, CancellationToken cancellationToken = default);
    Task CompleteAsync(Guid sessionId, string outputBlobPath, CancellationToken cancellationToken = default);
    Task ErrorAsync(Guid sessionId, string errorMessage, CancellationToken cancellationToken = default);
}
