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
            SessionId sessionId,
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
                SessionId = script.SessionId.Value,
                GeneratedAt = script.GeneratedAt,
                TotalSoundCount = script.TotalSoundCount,
                AverageDensitySeconds = script.AverageDensitySeconds,
                Entries = entries.Select(e => new ScriptEntryDto
                {
                    EntryId = e.EntryId.Value,
                    SessionId = e.SessionId.Value,
                    TimestampMs = e.TimestampMs,
                    SoundId = e.SoundId.Value,
                    SoundName = e.SoundName,
                    ActionVectorTags = e.ActionVectorTags,
                    SceneDescription = e.SceneDescription,
                    SelectionRationale = e.SelectionRationale,
                    IsIronic = e.IsIronic,
                    VisualEffect = e.VisualEffect,
                    EffectIntensity = e.EffectIntensity,
                    OverlayAssetId = e.OverlayAssetId,
                    PlacementType = e.PlacementType,
                    CaptionText = e.CaptionText,
                    CaptionPosition = e.CaptionPosition,
                    HotspotX = e.HotspotX,
                    HotspotY = e.HotspotY,
                    OverlayX = e.OverlayX,
                    OverlayY = e.OverlayY,
                    OverlayScale = e.OverlayScale,
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
            SessionId sessionId,
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
            SessionId sessionId,
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

        // GET /api/output/sessions/{id}/stream/source — inline stream for source video (comparison player)
        group.MapGet("/sessions/{sessionId:guid}/stream/source", async (
            SessionId sessionId,
            IVideoSessionRepository sessionRepository,
            BlobStorageService blobService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            if (string.IsNullOrEmpty(session.SourceBlobPath))
                return Results.NotFound(new { error = "SOURCE_NOT_FOUND", sessionId });

            try
            {
                var stream = await blobService.StreamBlobAsync(session.SourceBlobPath, ct);
                return Results.File(
                    stream,
                    contentType: "video/mp4",
                    enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        })
        .WithName("StreamSourceVideo")
        .WithTags("Output")
        .Produces(200)
        .Produces<object>(404)
        .Produces<object>(500);

        // DELETE /api/output/sessions/{id} — Wipe Buffer: delete session and all associated blobs
        group.MapDelete("/sessions/{sessionId:guid}", async (
            SessionId sessionId,
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


        // POST /api/output/sessions/{sessionId}/rerender — re-render video with modified director script
        group.MapPost("/sessions/{sessionId:guid}/rerender", async (
            SessionId sessionId,
            DirectorScriptDto updatedScript,
            IVideoSessionRepository sessionRepository,
            IDirectorScriptRepository scriptRepository,
            IRenderVideoCommand renderCommand,
            IEngineNotifier notifier,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            var entries = updatedScript.Entries.Select(e => new ScriptEntry
            {
                EntryId = new EntryId(e.EntryId != Guid.Empty ? e.EntryId : Guid.NewGuid()),
                SessionId = sessionId,
                TimestampMs = e.TimestampMs,
                SoundId = new SoundId(e.SoundId),
                SoundName = e.SoundName,
                ActionVectorTags = e.ActionVectorTags ?? [],
                SceneDescription = e.SceneDescription ?? string.Empty,
                SelectionRationale = e.SelectionRationale ?? string.Empty,
                IsIronic = e.IsIronic,
                VisualEffect = e.VisualEffect,
                EffectIntensity = e.EffectIntensity,
                OverlayAssetId = e.OverlayAssetId,
                PlacementType = e.PlacementType,
                CaptionText = e.CaptionText,
                CaptionPosition = e.CaptionPosition,
                HotspotX = e.HotspotX,
                HotspotY = e.HotspotY,
                OverlayX = e.OverlayX,
                OverlayY = e.OverlayY,
                OverlayScale = e.OverlayScale,
            }).ToList();

            var jsonOpts = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                WriteIndented = false,
            };

            var script = new DirectorScript
            {
                SessionId = sessionId,
                TotalSoundCount = entries.Count,
                AverageDensitySeconds = entries.Count > 0 && session.VideoDurationSeconds > 0
                    ? session.VideoDurationSeconds / entries.Count
                    : 0,
                EntriesJson = JsonSerializer.Serialize(entries, jsonOpts),
                GeneratedAt = DateTimeOffset.UtcNow,
            };

            await scriptRepository.SaveAsync(script, ct);
            await sessionRepository.UpdateStatusAsync(sessionId, userId, SessionStatus.Processing, cancellationToken: ct);

            // Trigger background render
            _ = Task.Run(async () =>
            {
                try
                {
                    await renderCommand.ExecuteAsync(sessionId, userId, session, script, CancellationToken.None);
                    await notifier.CompleteAsync(sessionId, session.OutputBlobPath ?? $"sessions/{sessionId}/output.mp4", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    await sessionRepository.UpdateStatusAsync(sessionId, userId, SessionStatus.Error, ex.Message, cancellationToken: CancellationToken.None);
                    await notifier.ErrorAsync(sessionId, $"RERENDER ERROR: {ex.Message}", CancellationToken.None);
                }
            });

            return Results.Accepted($"/api/output/sessions/{sessionId}", new
            {
                sessionId,
                status = "Rendering",
            });
        })
        .WithName("ReRenderSession")
        .WithTags("Output")
        .Produces<object>(202)
        .Produces<object>(404);

        return app;
    }

    /// <summary>
    /// Resolves the authenticated user's ID from claims.
    /// Falls back to <see cref="Guid.Empty"/> in unauthenticated/dev scenarios.
    /// </summary>
    private static UserId ResolveUserId(HttpContext httpContext)
    {
        return UserIdentityResolution.TryGetUserId(httpContext)
            ?? throw new InvalidOperationException("Authenticated user id claim is missing.");
    }
}
