// GoF: Observer Pattern — EngineHubClient exposes observable event streams backed by SignalR
using Microsoft.AspNetCore.SignalR.Client;
using PoMemeVideo.Shared.Models;

namespace PoMemeVideo.Client.Services;

/// <summary>
/// Manages the SignalR connection to EngineHub and exposes event streams for all server→client messages.
/// </summary>
public sealed class EngineHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public EngineHubClient(string hubUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(2000),
                TimeSpan.FromMilliseconds(5000),
                TimeSpan.FromMilliseconds(10000),
            ])
            .Build();

        _connection.On<string>("DirectorLogEntry", msg => DirectorLogEntry?.Invoke(msg));
        _connection.On<ScriptEntryDto>("DirectorScriptEntry", entry => DirectorScriptEntry?.Invoke(entry));
        _connection.On<string>("AuditEntry", msg => AuditEntry?.Invoke(msg));
        _connection.On<double, double>("HardwareMetrics", (lat, cpu) => HardwareMetrics?.Invoke(lat, cpu));
        _connection.On<string>("ProcessingComplete", path => ProcessingComplete?.Invoke(path));
        _connection.On<string>("ProcessingError", err => ProcessingError?.Invoke(err));
    }

    // ── Server → Client Events ────────────────────────────────────────────────

    /// <summary>Human-readable Director's Log line.</summary>
    public event Action<string>? DirectorLogEntry;

    /// <summary>A single ScriptEntry as it is built in real time.</summary>
    public event Action<ScriptEntryDto>? DirectorScriptEntry;

    /// <summary>Conflict-resolution and fallback audit event.</summary>
    public event Action<string>? AuditEntry;

    /// <summary>Inference latency (ms) and CPU load (%) — emitted every ~1 s.</summary>
    public event Action<double, double>? HardwareMetrics;

    /// <summary>Fired when the full Director's Script is complete.</summary>
    public event Action<string>? ProcessingComplete;

    /// <summary>Fired on unrecoverable engine error.</summary>
    public event Action<string>? ProcessingError;

    // ── Client → Server ───────────────────────────────────────────────────────

    public async Task JoinSessionAsync(Guid sessionId)
    {
        if (_connection.State == HubConnectionState.Disconnected)
            await _connection.StartAsync();

        await _connection.InvokeAsync("JoinSession", sessionId.ToString());
    }

    public async Task LeaveSessionAsync(Guid sessionId)
    {
        if (_connection.State != HubConnectionState.Disconnected)
            await _connection.InvokeAsync("LeaveSession", sessionId.ToString());
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
