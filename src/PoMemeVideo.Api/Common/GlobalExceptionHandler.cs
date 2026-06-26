using Microsoft.AspNetCore.Diagnostics;

namespace PoMemeVideo.Api.Common;

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
        // ASP.NET Core parameter-binding failures (e.g. invalid JSON body) throw
        // BadHttpRequestException with a proper StatusCode; map to that instead of 500.
        var statusCode = exception is Microsoft.AspNetCore.Http.BadHttpRequestException badReq
            ? badReq.StatusCode
            : StatusCodes.Status500InternalServerError;

        var logLevel = statusCode is >= 400 and < 500 ? LogLevel.Warning : LogLevel.Error;
        _logger.Log(logLevel, exception, "{ExceptionType} ({StatusCode}): {Message}",
            exception.GetType().Name, statusCode, exception.Message);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        var rfcType = statusCode switch
        {
            400 => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            404 => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        };

        object problem = _environment.IsDevelopment()
            ? new
            {
                type = rfcType,
                title = GetTitle(statusCode),
                status = statusCode,
                detail = exception.Message,
                exceptionType = exception.GetType().FullName,
                stackTrace = exception.StackTrace,
                traceId = httpContext.TraceIdentifier,
            }
            : new
            {
                type = rfcType,
                title = GetTitle(statusCode),
                status = statusCode,
                detail = statusCode >= 500
                    ? "An unexpected error occurred. Check logs for details."
                    : exception.Message,
                exceptionType = (string?)null,
                stackTrace = (string?)null,
                traceId = httpContext.TraceIdentifier,
            };

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        404 => "Not Found",
        _ => "Internal Server Error",
    };
}
