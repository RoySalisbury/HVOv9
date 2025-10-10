using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Mvc;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IDiagnosticsService _diagnosticsService;

    public DiagnosticsController(IDiagnosticsService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
    }

    [HttpGet("background-stacker")]
    [ProducesResponseType(typeof(BackgroundStackerMetricsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BackgroundStackerMetricsResponse>> GetBackgroundStackerMetricsAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetBackgroundStackerMetricsAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccessful)
        {
            return Ok(result.Value);
        }

        var error = result.Error;
        if (error is OperationCanceledException)
        {
            throw error;
        }

        return Problem(
            title: "Unable to retrieve background stacker diagnostics.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("background-stacker/history")]
    [ProducesResponseType(typeof(BackgroundStackerHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BackgroundStackerHistoryResponse>> GetBackgroundStackerHistoryAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetBackgroundStackerHistoryAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccessful)
        {
            return Ok(result.Value);
        }

        var error = result.Error;
        if (error is OperationCanceledException)
        {
            throw error;
        }

        return Problem(
            title: "Unable to retrieve historical background stacker diagnostics.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("filters")]
    [ProducesResponseType(typeof(FilterMetricsSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<FilterMetricsSnapshot>> GetFilterMetricsAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetFilterMetricsAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccessful)
        {
            return Ok(result.Value);
        }

        var error = result.Error;
        if (error is OperationCanceledException)
        {
            throw error;
        }

        return Problem(
            title: "Unable to retrieve filter telemetry.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
