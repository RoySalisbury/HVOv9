using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Models.System;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/configuration/system")]
public sealed class SystemConfigurationController : ControllerBase
{
    private readonly ISystemConfigurationService _configurationService;

    public SystemConfigurationController(ISystemConfigurationService configurationService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    [HttpGet("observatory")]
    [ProducesResponseType(typeof(SystemObservatoryConfigurationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemObservatoryConfigurationResponse>> GetObservatoryAsync(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _configurationService.GetObservatoryAsync(cancellationToken)).ConfigureAwait(false);

    [HttpPut("observatory")]
    [ProducesResponseType(typeof(SystemObservatoryConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SystemObservatoryConfigurationResponse>> UpdateObservatoryAsync([FromBody] UpdateSystemObservatoryRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _configurationService.UpdateObservatoryAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpGet("local-api")]
    [ProducesResponseType(typeof(SystemLocalApiConfigurationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemLocalApiConfigurationResponse>> GetLocalApiAsync(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _configurationService.GetLocalApiAsync(cancellationToken)).ConfigureAwait(false);

    [HttpPut("local-api")]
    [ProducesResponseType(typeof(SystemLocalApiConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SystemLocalApiConfigurationResponse>> UpdateLocalApiAsync([FromBody] UpdateSystemLocalApiRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _configurationService.UpdateLocalApiAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpGet("telemetry-retention")]
    [ProducesResponseType(typeof(SystemTelemetryRetentionConfigurationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemTelemetryRetentionConfigurationResponse>> GetTelemetryRetentionAsync(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _configurationService.GetTelemetryRetentionAsync(cancellationToken)).ConfigureAwait(false);

    [HttpPut("telemetry-retention")]
    [ProducesResponseType(typeof(SystemTelemetryRetentionConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SystemTelemetryRetentionConfigurationResponse>> UpdateTelemetryRetentionAsync([FromBody] UpdateSystemTelemetryRetentionRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _configurationService.UpdateTelemetryRetentionAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpGet("fits-export")]
    [ProducesResponseType(typeof(SystemFitsExportConfigurationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemFitsExportConfigurationResponse>> GetFitsExportAsync(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _configurationService.GetFitsExportAsync(cancellationToken)).ConfigureAwait(false);

    [HttpPut("fits-export")]
    [ProducesResponseType(typeof(SystemFitsExportConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SystemFitsExportConfigurationResponse>> UpdateFitsExportAsync([FromBody] UpdateSystemFitsExportRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _configurationService.UpdateFitsExportAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpGet("runtime")]
    [ProducesResponseType(typeof(RigRuntimeStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RigRuntimeStatusResponse>> GetRigRuntimeStatusAsync(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _configurationService.GetRigRuntimeStatusAsync(cancellationToken)).ConfigureAwait(false);

    [HttpPost("runtime/action")]
    [ProducesResponseType(typeof(RigRuntimeActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RigRuntimeActionResponse>> ExecuteRigRuntimeActionAsync([FromBody] RigRuntimeActionRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _configurationService.ExecuteRigRuntimeActionAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    private async Task<ActionResult<TResponse>> ExecuteAsync<TResponse>(Func<Task<HVO.Result<TResponse>>> operation)
    {
        var result = await operation().ConfigureAwait(false);
        if (result.IsSuccessful)
        {
            return Ok(result.Value);
        }

        var error = result.Error;
        if (error is InvalidOperationException)
        {
            return Problem(detail: error.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        return Problem(detail: error?.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
}
