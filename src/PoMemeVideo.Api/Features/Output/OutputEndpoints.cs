using PoMemeVideo.Shared;
using PoMemeVideo.Shared.Models;
using PoMemeVideo.Shared.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.Features.Output;

public static class OutputEndpoints
{
    public static IEndpointRouteBuilder MapOutputEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/output").RequireAuthorization();

        // GET /api/output/sessions/{id}/script — retrieve Director's Script from Table Storage
        group.MapGet("/sessions/{sessionId:guid}/script", async (
            Guid sessionId,
            IVideoSessionRepository sessionRepository,
            IDirectorScriptRepository scriptRepository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            var script = await scriptRepository.GetBySessionIdAsync(sessionId, ct);
            if (script is null)
                return Results.NotFound(new { error = "SCRIPT_NOT_FOUND", sessionId });

            // Deserialize stored entries (enums as strings)
            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() },
            };
            var entries = JsonSerializer.Deserialize<List<ScriptEntry>>(script.EntriesJson, jsonOpts) ?? [];

            var scriptDto = new DirectorScriptDto
            {
                SessionId = script.SessionId,
                GeneratedAt = script.GeneratedAt,
                TotalSoundCount = script.TotalSoundCount,
                AverageDensitySeconds = script.AverageDensitySeconds,
                Entries = entries.Select(e => new ScriptEntryDto
                {
                    EntryId = e.EntryId,
                    SessionId = e.SessionId,
                    TimestampMs = e.TimestampMs,
                    SoundId = e.SoundId,
                    SoundName = e.SoundName,
                    ActionVectorTags = e.ActionVectorTags,
                    SceneDescription = e.SceneDescription,
                    SelectionRationale = e.SelectionRationale,
                    IsIronic = e.IsIronic,
                    VisualEffect = e.VisualEffect,
                    EffectIntensity = e.EffectIntensity,
                    OverlayAssetId = e.OverlayAssetId,
                    PlacementType = e.PlacementType,
                }).ToList(),
            };

            return Results.Ok(scriptDto);
        })
        .WithName("GetDirectorScript")
        .WithTags("Output")
        .Produces<DirectorScriptDto>(200)
        .Produces<object>(404);

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
                    fileDownloadName: $"{PoMemeVideoNaming.ApplicationSlug}-{sessionId}.mp4",
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
        .Produces<object>(500);

        // GET /api/output/sessions/{id}/stream/video — inline stream for <video> element (no Content-Disposition: attachment)
        group.MapGet("/sessions/{sessionId:guid}/stream/video", async (
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
                    enableRangeProcessing: true);   // no fileDownloadName → Content-Disposition: inline
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("StreamVideo")
        .WithTags("Output")
        .Produces(200)
        .Produces<object>(404)
        .Produces<object>(500);

        // GET /api/output/sessions/{id}/download/script — download Director's Script as JSON
        group.MapGet("/sessions/{sessionId:guid}/download/script", async (
            Guid sessionId,
            IVideoSessionRepository sessionRepository,
            IDirectorScriptRepository scriptRepository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            var script = await scriptRepository.GetBySessionIdAsync(sessionId, ct);

            var jsonOpts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() },
                WriteIndented = true,
            };

            var payload = script is not null ? script.EntriesJson : "[]";
            var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            var stream = new MemoryStream(bytes);

            return Results.File(
                stream,
                contentType: "application/json",
                fileDownloadName: $"director-script-{sessionId}.json");
        })
        .WithName("DownloadDirectorScript")
        .WithTags("Output")
        .Produces(200)
        .Produces<object>(404);

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
        .Produces<object>(500);

        // GET /api/results — list all completed sessions for the current user
        group.MapGet("/results", async (
            IVideoSessionRepository sessionRepository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);
            var sessions = await sessionRepository.ListCompletedAsync(userId, ct);

            var dtos = sessions.Select(s => new VideoSessionDto
            {
                SessionId = s.SessionId,
                UserId = s.UserId,
                SourceBlobPath = s.SourceBlobPath,
                VideoDurationSeconds = s.VideoDurationSeconds,
                AggressiveVisuals = s.AggressiveVisuals,
                Status = s.Status,
                ErrorMessage = s.ErrorMessage,
                CreatedAt = s.CreatedAt,
                CompletedAt = s.CompletedAt,
                OutputBlobPath = s.OutputBlobPath,
            }).ToList();

            return Results.Ok(dtos);
        })
        .WithName("ListResults")
        .WithTags("Output")
        .Produces<List<VideoSessionDto>>(200);

        return app;
    }

    /// <summary>
    /// Resolves the authenticated user's ID from claims.
    /// Falls back to <see cref="Guid.Empty"/> in unauthenticated/dev scenarios.
    /// </summary>
    private static Guid ResolveUserId(HttpContext httpContext)
    {
        return UserIdentityResolution.TryGetUserId(httpContext)
            ?? throw new InvalidOperationException("Authenticated user id claim is missing.");
    }
}
