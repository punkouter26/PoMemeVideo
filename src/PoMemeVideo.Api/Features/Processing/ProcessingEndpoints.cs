// GoF: Command Pattern — initiates engine pipeline
using Microsoft.AspNetCore.Mvc;
using PoMemeVideo.Application.Processing;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Shared.Enums;

namespace PoMemeVideo.Api.Features.Processing;

public static class ProcessingEndpoints
{
    public static IEndpointRouteBuilder MapProcessingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/processing");

        group.MapPost("/sessions/{sessionId:guid}/initiate", async (
            Guid sessionId,
            [FromServices] IVideoSessionRepository sessions,
            [FromServices] RunEngineCommand command,
            HttpContext ctx,
            CancellationToken cancellationToken) =>
        {
            // For now, use a placeholder userId; real auth will populate this
            var userId = Guid.Empty;

            var session = await sessions.GetByIdAsync(sessionId, userId, cancellationToken);

            if (session is null)
                return Results.NotFound(new { error = $"Session {sessionId} not found." });

            if (session.Status != SessionStatus.Ingesting)
                return Results.Conflict(new
                {
                    error = $"Session {sessionId} is not in Ingesting state (current: {session.Status})."
                });

            // Fire and forget — client receives real-time updates via SignalR
            _ = Task.Run(
                () => command.ExecuteAsync(sessionId, userId, CancellationToken.None),
                CancellationToken.None);

            return Results.Accepted($"/api/processing/sessions/{sessionId}", new
            {
                sessionId,
                status = "Processing",
            });
        });

        return routes;
    }
}
