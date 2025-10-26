using System;
using System.Net;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HVO.SkyMonitorV5.RPi.Middleware;

/// <summary>
/// Global exception handler middleware that converts exceptions into ProblemDetails responses for SkyMonitor v5.
/// </summary>
public sealed class HvoServiceExceptionHandler : IExceptionHandler
{
    private readonly ILogger<HvoServiceExceptionHandler> _logger;
    private readonly IObservatoryClock _observatoryClock;

    public HvoServiceExceptionHandler(
        ILogger<HvoServiceExceptionHandler> logger,
        IObservatoryClock observatoryClock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _observatoryClock = observatoryClock ?? throw new ArgumentNullException(nameof(observatoryClock));
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = MapExceptionToResponse(exception);

        _logger.LogError(exception, "Unhandled exception encountered in SkyMonitor v5 service: {Message}", exception.Message);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
    problemDetails.Extensions["timestamp"] = _observatoryClock.LocalNow;

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
