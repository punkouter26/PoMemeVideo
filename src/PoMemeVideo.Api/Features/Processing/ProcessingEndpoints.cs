// GoF: Command Pattern — initiates engine pipeline
using Microsoft.AspNetCore.Mvc;
using PoMemeVideo.Api.Features.Auth;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Api.Features.Processing;

public static class ProcessingEndpoints
{
    public static IEndpointRouteBuilder MapProcessingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/processing").RequireAuthorization();

        group.MapPost("/sessions/{sessionId:guid}/initiate", async (
            Guid sessionId,
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

            if (session.Status != SessionStatus.Ingesting)
                return Results.Conflict(new
                {
                    error = $"Session {sessionId} is not in Ingesting state (current: {session.Status})."
                });

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
