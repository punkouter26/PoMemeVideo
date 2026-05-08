using System.Collections.Concurrent;
using System.Threading.Channels;
using PoMemeVideo.Application.Processing;

namespace PoMemeVideo.Api.Features.Processing;

public interface IEngineRunDispatcher
{
    bool TryQueue(Guid sessionId, Guid userId);
}

internal sealed class EngineRunDispatcher : BackgroundService, IEngineRunDispatcher
{
    private readonly Channel<EngineRunRequest> _queue = Channel.CreateUnbounded<EngineRunRequest>();
    private readonly ConcurrentDictionary<Guid, byte> _queuedOrRunning = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EngineRunDispatcher> _logger;

    public EngineRunDispatcher(IServiceScopeFactory scopeFactory, ILogger<EngineRunDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool TryQueue(Guid sessionId, Guid userId)
    {
        if (!_queuedOrRunning.TryAdd(sessionId, 0))
            return false;

        if (!_queue.Writer.TryWrite(new EngineRunRequest(sessionId, userId)))
        {
            _queuedOrRunning.TryRemove(sessionId, out _);
            return false;
        }

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var command = scope.ServiceProvider.GetRequiredService<RunEngineCommand>();
                    await command.ExecuteAsync(request.SessionId, request.UserId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Host is shutting down; exit the loop cleanly.
                    _queuedOrRunning.TryRemove(request.SessionId, out _);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Queued engine execution failed for session {SessionId}", request.SessionId);
                }
                finally
                {
                    _queuedOrRunning.TryRemove(request.SessionId, out _);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // ReadAllAsync throws when the token fires; this is expected on shutdown.
        }
    }

    private sealed record EngineRunRequest(Guid SessionId, Guid UserId);
}