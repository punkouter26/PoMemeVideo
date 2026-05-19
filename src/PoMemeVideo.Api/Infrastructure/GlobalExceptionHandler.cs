using Microsoft.AspNetCore.Diagnostics;

namespace PoMemeVideo.Api.Infrastructure;

// GoF: Chain of Responsibility — sits in the ASP.NET Core exception handler pipeline.
// SOLID: Single Responsibility — all unhandled-exception formatting is isolated here.
/// <summary>
/// Catches all unhandled exceptions and returns RFC 7807 Problem Details JSON.
/// In Development mode, includes the full stack trace for rapid LLM-assisted debugging.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception {ExceptionType}: {Message}",
            exception.GetType().Name, exception.Message);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";

        object problem = _environment.IsDevelopment()
            ? new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "Internal Server Error",
                status = StatusCodes.Status500InternalServerError,
                detail = exception.Message,
                exceptionType = exception.GetType().FullName,
                stackTrace = exception.StackTrace,
                traceId = httpContext.TraceIdentifier,
            }
            : new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "Internal Server Error",
                status = StatusCodes.Status500InternalServerError,
                detail = "An unexpected error occurred. Check logs for details.",
                exceptionType = (string?)null,
                stackTrace = (string?)null,
                traceId = httpContext.TraceIdentifier,
            };

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
