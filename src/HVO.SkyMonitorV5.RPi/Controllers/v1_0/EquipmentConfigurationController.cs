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
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/configuration/equipment")]
public sealed class EquipmentConfigurationController : ControllerBase
{
    private readonly IEquipmentConfigurationService _equipmentService;
    private readonly ILogger<EquipmentConfigurationController>? _logger;

    public EquipmentConfigurationController(IEquipmentConfigurationService equipmentService, ILogger<EquipmentConfigurationController>? logger = null)
    {
        _equipmentService = equipmentService ?? throw new ArgumentNullException(nameof(equipmentService));
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EquipmentCatalogResponse>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        _logger?.LogDebug("Equipment catalog request received.");
        return await ExecuteAsync(() => _equipmentService.GetCatalogAsync(cancellationToken)).ConfigureAwait(false);
    }

    [HttpPost("rigs")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> CreateRigAsync([FromBody] CreateRigRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        _logger?.LogInformation(
            "Create rig request received. Key={Key}, CameraKey={CameraKey}, OpticsKey={OpticsKey}, IsActive={IsActive}.",
            request.Key,
            request.CameraKey,
            request.OpticsKey,
            request.IsActive);

        return await ExecuteAsync(() => _equipmentService.CreateRigAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpPut("rigs/{rigId:int}")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> UpdateRigAsync(int rigId, [FromBody] UpdateRigRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        _logger?.LogInformation(
            "Update rig request received. RigId={RigId}, Revision={Revision}, CameraKey={CameraKey}, OpticsKey={OpticsKey}, IsActive={IsActive}.",
            rigId,
            request.Revision,
            request.CameraKey,
            request.OpticsKey,
            request.IsActive);

        return await ExecuteAsync(() => _equipmentService.UpdateRigAsync(rigId, request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpDelete("rigs/{rigId:int}")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> DeleteRigAsync(int rigId, [FromQuery] long? revision, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Delete rig request received. RigId={RigId}, Revision={Revision}.", rigId, revision);
        return await ExecuteAsync(() => _equipmentService.DeleteRigAsync(rigId, revision, cancellationToken)).ConfigureAwait(false);
    }



    [HttpPost("cameras")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> CreateCameraAsync([FromBody] CreateCameraRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        _logger?.LogInformation(
            "Create camera request received. Key={Key}, DriverId={DriverId}, IsActive={IsActive}.",
            request.Key,
            request.DriverId,
            request.IsActive);

        return await ExecuteAsync(() => _equipmentService.CreateCameraAsync(request, cancellationToken)).ConfigureAwait(false);
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

        _logger?.LogInformation(
            "Update camera request received. CameraId={CameraId}, Revision={Revision}, DriverId={DriverId}, IsActive={IsActive}.",
            cameraId,
            request.Revision,
            request.DriverId,
            request.IsActive);

        return await ExecuteAsync(() => _equipmentService.UpdateCameraAsync(cameraId, request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpPost("optics")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> CreateOpticsAsync([FromBody] CreateOpticsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        _logger?.LogInformation(
            "Create optics request received. Key={Key}, ProjectionModel={ProjectionModel}, FocalLength={FocalLength}, IsActive={IsActive}.",
            request.Key,
            request.ProjectionModel,
            request.FocalLengthMillimeters,
            request.IsActive);

        return await ExecuteAsync(() => _equipmentService.CreateOpticsAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpPut("optics/{opticsId:int}")]
    [ProducesResponseType(typeof(EquipmentCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EquipmentCatalogResponse>> UpdateOpticsAsync(int opticsId, [FromBody] UpdateOpticsRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        _logger?.LogInformation(
            "Update optics request received. OpticsId={OpticsId}, Revision={Revision}, ProjectionModel={ProjectionModel}, IsActive={IsActive}.",
            opticsId,
            request.Revision,
            request.ProjectionModel,
            request.IsActive);

        return await ExecuteAsync(() => _equipmentService.UpdateOpticsAsync(opticsId, request, cancellationToken)).ConfigureAwait(false);
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
