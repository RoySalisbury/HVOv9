using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using HVO;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/configuration/drivers")]
public sealed class DriverConfigurationController : ControllerBase
{
    private readonly IEquipmentConfigurationService _equipmentService;

    public DriverConfigurationController(IEquipmentConfigurationService equipmentService)
    {
        _equipmentService = equipmentService ?? throw new ArgumentNullException(nameof(equipmentService));
    }

    [HttpGet]
    [ProducesResponseType(typeof(CameraDriverCatalogResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CameraDriverCatalogResponse>> GetCameraDriversAsync(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _equipmentService.GetCameraDriversAsync(cancellationToken)).ConfigureAwait(false);

    private async Task<ActionResult<CameraDriverCatalogResponse>> ExecuteAsync(Func<Task<Result<CameraDriverCatalogResponse>>> operation)
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
