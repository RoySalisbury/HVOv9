using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Models.Catalog;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Models.Rigs;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/optics")]
public sealed class OpticsConfigurationController : ControllerBase
{
    private readonly IOpticsConfigurationService _opticsService;

    public OpticsConfigurationController(IOpticsConfigurationService opticsService)
    {
        _opticsService = opticsService ?? throw new ArgumentNullException(nameof(opticsService));
    }

    [HttpGet]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EquipmentCatalogResponse>> GetCatalogAsync(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _opticsService.GetCatalogAsync(cancellationToken)).ConfigureAwait(false);

    [HttpPost]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> CreateRigAsync([FromBody] CreateRigRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _opticsService.CreateRigAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpPut("{rigId:int}")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> UpdateRigAsync(int rigId, [FromBody] UpdateRigRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _opticsService.UpdateRigAsync(rigId, request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpDelete("{rigId:int}")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> DeleteRigAsync(int rigId, [FromQuery] long? revision, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _opticsService.DeleteRigAsync(rigId, revision, cancellationToken)).ConfigureAwait(false);

    [HttpPost("cameras")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> CreateCameraAsync([FromBody] CreateCameraRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _opticsService.CreateCameraAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpPut("cameras/{cameraId:int}")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> UpdateCameraAsync(int cameraId, [FromBody] UpdateCameraRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _opticsService.UpdateCameraAsync(cameraId, request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpPost("lenses")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> CreateOpticsAsync([FromBody] CreateOpticsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _opticsService.CreateOpticsAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpPut("lenses/{opticsId:int}")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> UpdateOpticsAsync(int opticsId, [FromBody] UpdateOpticsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _opticsService.UpdateOpticsAsync(opticsId, request, cancellationToken)).ConfigureAwait(false);
    }

    private async Task<ActionResult<EquipmentCatalogResponse>> ExecuteAsync(Func<Task<HVO.Result<EquipmentCatalogResponse>>> operation)
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
