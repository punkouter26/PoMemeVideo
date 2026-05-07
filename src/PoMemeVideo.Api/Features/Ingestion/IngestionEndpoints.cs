using PoMemeVideo.Application.Ingestion;
using PoMemeVideo.Domain.Interfaces;
using PoMemeVideo.Infrastructure.AzureStorage;
using PoMemeVideo.Shared.Models;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Api.Features.Ingestion;

public static class IngestionEndpoints
{
    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/ingestion/sas — generate SAS token for direct browser-to-Blob upload
        app.MapPost("/api/ingestion/sas", async (
            SasRequest request,
            IngestVideoCommand command,
            BlobServiceClientFactory blobFactory,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            IngestVideoResult result;
            try
            {
                result = await command.ExecuteAsync(request.FileName, request.FileSizeBytes, userId, ct);
            }
            catch (VideoIngestionValidationException ex) when (ex.ErrorCode == "INVALID_EXTENSION")
            {
                return Results.BadRequest(new
                {
                    error = ex.ErrorCode,
                    message = ex.Message,
                    allowedExtensions = IngestVideoCommand.AllowedExtensions,
                });
            }
            catch (VideoIngestionValidationException ex) when (ex.ErrorCode == "FILE_TOO_LARGE")
            {
                return Results.BadRequest(new
                {
                    error = ex.ErrorCode,
                    message = ex.Message,
                    maxBytes = ex.MaxBytes,
                    receivedBytes = ex.ReceivedBytes,
                });
            }

            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
            var sasUrl = await blobFactory.GenerateUploadSasUriAsync(result.SourceBlobPath, expiresAt, ct);

            return Results.Ok(new
            {
                sessionId = result.SessionId,
                sasUrl = sasUrl.ToString(),
                expiresAt,
            });
        })
        .WithName("GenerateSasToken")
        .WithTags("Ingestion")
        .Produces<object>(200)
        .Produces<object>(400)
        .AllowAnonymous();

        // POST /api/ingestion/sessions — confirm upload complete, finalise session metadata
        app.MapPost("/api/ingestion/sessions", async (
            SessionConfirmRequest request,
            IVideoSessionRepository repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await repository.GetByIdAsync(request.SessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId = request.SessionId });

            // Update mutable fields confirmed after upload
            session.SourceBlobPath = request.BlobPath;
            session.VideoDurationSeconds = request.VideoDurationSeconds;
            session.AggressiveVisuals = request.AggressiveVisuals;

            // Persist the updated session by recreating (replace) or use UpdateStatus
            await repository.UpdateStatusAsync(session.SessionId, userId, session.Status, cancellationToken: ct);

            // Persist duration/aggressive flag via a full update through the table entity
            // (UpdateStatusAsync only updates status; for other fields we recreate the entity via the infrastructure layer)
            // Since the full update path isn't exposed by the interface, we persist via a workaround:
            // delete + recreate is safe here — session is still in Ingesting status
            await repository.DeleteAsync(session.SessionId, userId, ct);
            await repository.CreateAsync(session, ct);

            return Results.Created(
                $"/api/ingestion/sessions/{session.SessionId}",
                new { sessionId = session.SessionId, status = session.Status.ToString() });
        })
        .WithName("ConfirmUpload")
        .WithTags("Ingestion")
        .Produces<object>(201)
        .Produces<object>(404)
        .AllowAnonymous();

        // POST /api/ingestion/sessions/{sessionId}/frames — upload raw keyframe images for AI vision
        app.MapPost("/api/ingestion/sessions/{sessionId:guid}/frames", async (
            Guid sessionId,
            FrameUploadRequest request,
            IVideoSessionRepository sessionRepository,
            IBlobStorageService blobs,
            IAiVisionService vision,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);
            var session = await sessionRepository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            // Delete any previous frames / labels for this session
            await blobs.DeleteBlobsByPrefixAsync($"sessions/{sessionId}/frames/", ct);

            // Store raw PNG frames
            var base64Images = new List<string>(request.Frames.Count);
            for (var i = 0; i < request.Frames.Count; i++)
            {
                var b64 = request.Frames[i];
                var comma = b64.IndexOf(',');
                if (comma >= 0) b64 = b64[(comma + 1)..];
                base64Images.Add(b64);

                var bytes = Convert.FromBase64String(b64);
                using var ms = new MemoryStream(bytes);
                await blobs.UploadBlobAsync($"sessions/{sessionId}/frames/frame_{i:D4}.png", ms, "image/png", ct);
            }

            // Run vision analysis immediately so labels are ready before INITIATE
            var visionLabels = Array.Empty<(double TimestampSeconds, string Label)>();
            if (base64Images.Count > 0)
            {
                try { visionLabels = await vision.AnalyseAsync([.. base64Images], ct); }
                catch { /* vision unavailable — engine will fall back to time-based placement */ }
            }

            // Persist labels as a JSON blob so the engine can read them without re-calling the AI
            var labelsJson = JsonSerializer.Serialize(
                visionLabels.Select(v => new { timestamp_seconds = v.TimestampSeconds, label = v.Label }).ToArray());
            using var labelsStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(labelsJson));
            await blobs.UploadBlobAsync($"sessions/{sessionId}/vision-labels.json", labelsStream, "application/json", ct);

            return Results.Ok(new
            {
                sessionId,
                framesStored = base64Images.Count,
                visionLabels = visionLabels.Select(v => new { timestampSeconds = v.TimestampSeconds, label = v.Label }).ToArray(),
            });
        })
        .WithName("UploadFrames")
        .WithTags("Ingestion")
        .Produces<object>(200)
        .Produces<object>(404)
        .AllowAnonymous();

        // GET /api/ingestion/sessions/{sessionId} — retrieve session status
        app.MapGet("/api/ingestion/sessions/{sessionId:guid}", async (
            Guid sessionId,
            IVideoSessionRepository repository,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userId = ResolveUserId(httpContext);

            var session = await repository.GetByIdAsync(sessionId, userId, ct);
            if (session is null)
                return Results.NotFound(new { error = "SESSION_NOT_FOUND", sessionId });

            var dto = new VideoSessionDto
            {
                SessionId = session.SessionId,
                UserId = session.UserId,
                SourceBlobPath = session.SourceBlobPath,
                VideoDurationSeconds = session.VideoDurationSeconds,
                AggressiveVisuals = session.AggressiveVisuals,
                Status = session.Status,
                ErrorMessage = session.ErrorMessage,
                CreatedAt = session.CreatedAt,
                CompletedAt = session.CompletedAt,
                OutputBlobPath = session.OutputBlobPath,
            };

            return Results.Ok(dto);
        })
        .WithName("GetSession")
        .WithTags("Ingestion")
        .Produces<VideoSessionDto>(200)
        .Produces<object>(404)
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
        return Guid.Empty; // dev fallback — replaced by proper auth in Phase 7
    }
}

public sealed record SasRequest(string FileName, long FileSizeBytes);

public sealed record SessionConfirmRequest(
    Guid SessionId,
    string BlobPath,
    double VideoDurationSeconds,
    bool AggressiveVisuals);

public sealed record FrameUploadRequest(
    [property: JsonPropertyName("frames")] IReadOnlyList<string> Frames);
