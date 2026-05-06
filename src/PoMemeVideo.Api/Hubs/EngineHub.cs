using Microsoft.AspNetCore.SignalR;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Models;

namespace PoMemeVideo.Api.Hubs;

public class EngineHub : Hub
{
    /// <summary>
    /// Client→Server: Join the SignalR group for the given session.
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session-{sessionId}");
    }

    /// <summary>
    /// Client→Server: Leave the SignalR group for the given session.
    /// </summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session-{sessionId}");
    }
}

/// <summary>
/// Implements IEngineNotifier by sending messages to the appropriate SignalR group.
/// </summary>
public class EngineHubNotifier : IEngineNotifier
{
    private readonly IHubContext<EngineHub> _hubContext;

    public EngineHubNotifier(IHubContext<EngineHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task DirectorLogAsync(Guid sessionId, string message, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("DirectorLogEntry", message, cancellationToken);

    public Task DirectorScriptAsync(Guid sessionId, ScriptEntryDto entry, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("DirectorScriptEntry", entry, cancellationToken);

    public Task AuditAsync(Guid sessionId, string message, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("AuditEntry", message, cancellationToken);

    public Task HardwareMetricsAsync(Guid sessionId, double inferenceLatencyMs, double cpuLoadPercent, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("HardwareMetrics", inferenceLatencyMs, cpuLoadPercent, cancellationToken);

    public Task CompleteAsync(Guid sessionId, string outputBlobPath, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("ProcessingComplete", outputBlobPath, cancellationToken);

    public Task ErrorAsync(Guid sessionId, string errorMessage, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("ProcessingError", errorMessage, cancellationToken);

    public Task BrowserLLMInferenceRequestAsync(Guid sessionId, string payloadJson, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("BrowserLLMInferenceRequest", payloadJson, cancellationToken);
}
