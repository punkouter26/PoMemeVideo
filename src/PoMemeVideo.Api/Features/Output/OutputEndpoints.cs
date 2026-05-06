using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure.AzureStorage;
using PoMemeVideo.Shared.Models;
using PoMemeVideo.Shared.Enums;
using System.Security.Claims;

namespace PoMemeVideo.Api.Features.Output;

public static class OutputEndpoints
{
    public static IEndpointRouteBuilder MapOutputEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/output");

        // GET /api/output/sessions/{id}/script — retrieve Director's Script from Table Storage
        group.MapGet("/sessions/{sessionId:guid}/script", async (
            Guid sessionId,
            IVideoSessionRepository sessionRepository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);
            
            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            // For now, return a placeholder DirectorScript DTO
            // In a full implementation, this would load from a DirectorScriptRepository
            // For Phase 5, we construct a DTO from the session's OutputBlobPath
            var scriptDto = new DirectorScriptDto
            {
                SessionId = session.SessionId,
                GeneratedAt = DateTimeOffset.UtcNow,
                TotalSoundCount = 1,
                AverageDensitySeconds = 2.0,
                Entries = new List<ScriptEntryDto>()
                {
                    new ScriptEntryDto
                    {
                        EntryId = Guid.NewGuid(),
                        SessionId = session.SessionId,
                        TimestampMs = 2000,
                        SoundId = Guid.NewGuid(),
                        ActionVectorTags = new[] { "impact", "motion" },
                        SelectionRationale = "Matched falling motion at 2s",
                        IsIronic = false,
                        VisualEffect = VisualEffectType.DeepFry,
                        EffectIntensity = 1.0,
                        PlacementType = PlacementType.Triggered
                    }
                }
            };

            return Results.Ok(scriptDto);
        })
        .WithName("GetDirectorScript")
        .WithTags("Output")
        .Produces<DirectorScriptDto>(200)
        .Produces<object>(404)
        .AllowAnonymous();

        // GET /api/output/sessions/{id}/download/video — stream output MP4
        group.MapGet("/sessions/{sessionId:guid}/download/video", async (
            Guid sessionId,
            IVideoSessionRepository sessionRepository,
            BlobStorageService blobService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            if (string.IsNullOrEmpty(session.OutputBlobPath))
                return Results.NotFound(new { error = "OUTPUT_NOT_READY", sessionId });

            try
            {
                var stream = await blobService.StreamBlobAsync(session.OutputBlobPath, ct);
                return Results.File(
                    stream,
                    contentType: "video/mp4",
                    fileDownloadName: $"pomemevideo-{sessionId}.mp4",
                    enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("DownloadVideo")
        .WithTags("Output")
        .Produces(200)
        .Produces<object>(404)
        .Produces<object>(500)
        .AllowAnonymous();

        // GET /api/output/sessions/{id}/download/script — download Director's Script as JSON
        group.MapGet("/sessions/{sessionId:guid}/download/script", async (
            Guid sessionId,
            IVideoSessionRepository sessionRepository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            // Retrieve the script DTO (same logic as /script endpoint)
            var scriptDto = new DirectorScriptDto
            {
                SessionId = session.SessionId,
                GeneratedAt = DateTimeOffset.UtcNow,
                TotalSoundCount = 1,
                AverageDensitySeconds = 2.0,
                Entries = new List<ScriptEntryDto>()
                {
                    new ScriptEntryDto
                    {
                        EntryId = Guid.NewGuid(),
                        SessionId = session.SessionId,
                        TimestampMs = 2000,
                        SoundId = Guid.NewGuid(),
                        ActionVectorTags = new[] { "impact", "motion" },
                        SelectionRationale = "Matched falling motion at 2s",
                        IsIronic = false,
                        VisualEffect = VisualEffectType.DeepFry,
                        EffectIntensity = 1.0,
                        PlacementType = PlacementType.Triggered
                    }
                }
            };

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(scriptDto);
            var bytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);
            var stream = new MemoryStream(bytes);

            return Results.File(
                stream,
                contentType: "application/json",
                fileDownloadName: $"director-script-{sessionId}.json");
        })
        .WithName("DownloadDirectorScript")
        .WithTags("Output")
        .Produces(200)
        .Produces<object>(404)
        .AllowAnonymous();

        // DELETE /api/output/sessions/{id} — Wipe Buffer: delete session and all associated blobs
        group.MapDelete("/sessions/{sessionId:guid}", async (
            Guid sessionId,
            IVideoSessionRepository sessionRepository,
            BlobStorageService blobService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            try
            {
                // Delete all blobs under sessions/{sessionId}/
                await blobService.DeleteBlobsByPrefixAsync($"sessions/{sessionId}/", ct);

                // Delete session record from Table Storage
                await sessionRepository.DeleteAsync(sessionId, userId, ct);

                return Results.NoContent();
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("WipeBuffer")
        .WithTags("Output")
        .Produces(204)
        .Produces<object>(404)
        .Produces<object>(500)
        .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Resolves the authenticated user's ID from claims.
    /// Falls back to <see cref="Guid.Empty"/> in unauthenticated/dev scenarios.
    /// </summary>
    private static Guid ResolveUserId(HttpContext httpContext)
    {
        var claim = httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (claim is not null && Guid.TryParse(claim, out var id))
            return id;
        return Guid.Empty; // dev fallback
    }
}
