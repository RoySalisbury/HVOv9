using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IOptionsMonitor<FrameExportOptions> _frameExportOptions;
    private readonly ILogger<DiagnosticsController>? _logger;

    public DiagnosticsController(IDiagnosticsService diagnosticsService, IOptionsMonitor<FrameExportOptions> frameExportOptions, ILogger<DiagnosticsController>? logger = null)
    {
        _diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
        _frameExportOptions = frameExportOptions ?? throw new ArgumentNullException(nameof(frameExportOptions));
        _logger = logger;
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

    [HttpGet("frames/composed")]
    [ProducesResponseType(typeof(ComposedFrameHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ComposedFrameHistoryResponse>> GetComposedFrameHistoryAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetComposedFrameHistoryAsync(cancellationToken).ConfigureAwait(false);

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
            title: "Unable to retrieve composed frame history.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("system")]
    [ProducesResponseType(typeof(SystemDiagnosticsSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SystemDiagnosticsSnapshot>> GetSystemDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetSystemDiagnosticsAsync(cancellationToken).ConfigureAwait(false);

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
            title: "Unable to retrieve system diagnostics.",
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

    [HttpGet("remote-dispatch")]
    [ProducesResponseType(typeof(RemoteDispatchMetricsSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RemoteDispatchMetricsSnapshot>> GetRemoteDispatchMetricsAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetRemoteDispatchMetricsAsync(cancellationToken).ConfigureAwait(false);

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
            title: "Unable to retrieve remote dispatch metrics.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("remote-dispatch/history")]
    [ProducesResponseType(typeof(RemoteDispatchHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RemoteDispatchHistoryResponse>> GetRemoteDispatchHistoryAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetRemoteDispatchHistoryAsync(cancellationToken).ConfigureAwait(false);

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
            title: "Unable to retrieve remote dispatch history.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("frame-exports")]
    [ProducesResponseType(typeof(FrameExportMetricsSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<FrameExportMetricsSnapshot>> GetFrameExportMetricsAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetFrameExportMetricsAsync(cancellationToken).ConfigureAwait(false);

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
            title: "Unable to retrieve frame export metrics.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("frame-exports/history")]
    [ProducesResponseType(typeof(FrameExportHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<FrameExportHistoryResponse>> GetFrameExportHistoryAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetFrameExportHistoryAsync(cancellationToken).ConfigureAwait(false);

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
            title: "Unable to retrieve frame export history.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("data-stores")]
    [ProducesResponseType(typeof(DataStoreMetricsSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DataStoreMetricsSnapshot>> GetDataStoreMetricsAsync(CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetDataStoreMetricsAsync(cancellationToken).ConfigureAwait(false);

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
            title: "Unable to retrieve data store diagnostics.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("telemetry-events")]
    [ProducesResponseType(typeof(TelemetryEventPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TelemetryEventPage>> GetTelemetryEventsAsync(
        [FromQuery] long? afterId,
        [FromQuery] long? beforeId,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _diagnosticsService.GetTelemetryEventsAsync(afterId, beforeId, pageSize, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccessful)
        {
            return Ok(result.Value);
        }

        var error = result.Error;
        if (error is OperationCanceledException)
        {
            throw error;
        }

        if (error is ArgumentException)
        {
            return Problem(
                title: "Invalid telemetry event query.",
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Problem(
            title: "Unable to retrieve telemetry events.",
            detail: error?.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    [HttpGet("frame-export")]
    [ProducesResponseType(typeof(DiagnosticsFrameExportResponse), StatusCodes.Status200OK)]
    public ActionResult<DiagnosticsFrameExportResponse> GetFrameExportConfiguration()
    {
        var options = _frameExportOptions.CurrentValue;

        var raw = options.GetStageOptions(Exports.FrameExportStage.Raw);
        var processed = options.GetStageOptions(Exports.FrameExportStage.Processed);

        var response = new DiagnosticsFrameExportResponse
        {
            Raw = DiagnosticsFrameExportResponse.StageInfo.FromStageOptions(raw, Exports.FrameExportStage.Raw),
            Processed = DiagnosticsFrameExportResponse.StageInfo.FromStageOptions(processed, Exports.FrameExportStage.Processed)
        };

        return Ok(response);
    }
}
