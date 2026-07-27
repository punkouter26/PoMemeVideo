using Microsoft.AspNetCore.SignalR;
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

    public Task DirectorLogAsync(SessionId sessionId, string message, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("DirectorLogEntry", message, cancellationToken);

    public Task DirectorScriptAsync(SessionId sessionId, ScriptEntryDto entry, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("DirectorScriptEntry", entry, cancellationToken);

    public Task AuditAsync(SessionId sessionId, string message, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("AuditEntry", message, cancellationToken);

    public Task HardwareMetricsAsync(SessionId sessionId, double inferenceLatencyMs, double cpuLoadPercent, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("HardwareMetrics", inferenceLatencyMs, cpuLoadPercent, cancellationToken);

    public Task CompleteAsync(SessionId sessionId, string outputBlobPath, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("ProcessingComplete", outputBlobPath, cancellationToken);

    public Task ErrorAsync(SessionId sessionId, string errorMessage, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("ProcessingError", errorMessage, cancellationToken);

    public Task BrowserLLMInferenceRequestAsync(SessionId sessionId, string payloadJson, CancellationToken cancellationToken = default)
        => _hubContext.Clients.Group($"session-{sessionId}")
            .SendAsync("BrowserLLMInferenceRequest", payloadJson, cancellationToken);
}
