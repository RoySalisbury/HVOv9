using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace HVO.SkyMonitorV4.RPi.Middleware;

/// <summary>
/// Global exception handler middleware for SkyMonitor services that converts exceptions to ProblemDetails responses.
/// </summary>
public sealed class HvoServiceExceptionHandler : IExceptionHandler
{
    private readonly ILogger<HvoServiceExceptionHandler> _logger;

    public HvoServiceExceptionHandler(ILogger<HvoServiceExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = MapExceptionToResponse(exception);

        _logger.LogError(exception, "Unhandled exception encountered in SkyMonitor service: {Message}", exception.Message);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static (int StatusCode, string Title) MapExceptionToResponse(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException => ((int)HttpStatusCode.BadRequest, "Bad Request"),
            ArgumentException => ((int)HttpStatusCode.BadRequest, "Bad Request"),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "Unauthorized"),
            NotImplementedException => ((int)HttpStatusCode.NotImplemented, "Not Implemented"),
            TimeoutException => ((int)HttpStatusCode.RequestTimeout, "Request Timeout"),
            _ => ((int)HttpStatusCode.InternalServerError, "Internal Server Error")
        };
    }
}
