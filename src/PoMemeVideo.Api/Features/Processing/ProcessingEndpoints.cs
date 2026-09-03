// GoF: Command Pattern — initiates engine pipeline
using Microsoft.AspNetCore.Mvc;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Api.Features.Processing;

public static class ProcessingEndpoints
{
    public static IEndpointRouteBuilder MapProcessingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/processing").RequireAuthorization();

        group.MapPost("/sessions/{sessionId:guid}/initiate", async (
            SessionId sessionId,
            [FromServices] IVideoSessionRepository sessions,
            [FromServices] IEngineRunDispatcher dispatcher,
            HttpContext ctx,
            CancellationToken cancellationToken) =>
        {
            var userId = UserIdentityResolution.TryGetUserId(ctx);
            if (userId is null)
                return Results.Unauthorized();

            var session = await sessions.GetByIdAsync(sessionId, userId.Value, cancellationToken);

            if (session is null)
                return Results.NotFound(new { error = $"Session {sessionId} not found." });

            // Retry is allowed from any state where the engine isn't actively running. The
            // previous build only accepted Ingesting|Error, so a session stuck in Processing
            // (e.g. the BrowserLLM 90-second timeout was still running when the user clicked
            // "Retry with Safe Fallback Mode") got a 409 and the user had no way out.
            // Allow Processing too: force the row back to Ingesting so the engine re-runs,
            // and let the dispatcher refuse the queue if the in-memory run is still live.
            if (session.Status is not (SessionStatus.Ingesting or SessionStatus.Error or SessionStatus.Processing))
            {
                return Results.Conflict(new
                {
                    error = $"Session {sessionId} is not in a retryable state (current: {session.Status})."
                });
            }

            if (session.Status is SessionStatus.Error or SessionStatus.Processing)
            {
                await sessions.UpdateStatusAsync(sessionId, userId.Value, SessionStatus.Ingesting, errorMessage: null, cancellationToken: cancellationToken);
            }

            if (!dispatcher.TryQueue(sessionId, userId.Value))
                return Results.Conflict(new { error = $"Session {sessionId} is already queued or running." });

            return Results.Accepted($"/api/processing/sessions/{sessionId}", new
            {
                sessionId,
                status = "Processing",
            });
        });

        return routes;
    }
}
