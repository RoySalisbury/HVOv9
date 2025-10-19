using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Models.Optics;
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
    [ProducesResponseType(typeof(OpticsCatalogResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpticsCatalogResponse>> GetCatalogAsync(CancellationToken cancellationToken)
        => await ExecuteAsync(() => _opticsService.GetCatalogAsync(cancellationToken)).ConfigureAwait(false);

    [HttpPost]
    [ProducesResponseType(typeof(OpticsCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OpticsCatalogResponse>> CreateRigAsync([FromBody] CreateOpticsRigRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _opticsService.CreateRigAsync(request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpPut("{rigId:int}")]
    [ProducesResponseType(typeof(OpticsCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OpticsCatalogResponse>> UpdateRigAsync(int rigId, [FromBody] UpdateOpticsRigRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await ExecuteAsync(() => _opticsService.UpdateRigAsync(rigId, request, cancellationToken)).ConfigureAwait(false);
    }

    [HttpDelete("{rigId:int}")]
    [ProducesResponseType(typeof(OpticsCatalogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OpticsCatalogResponse>> DeleteRigAsync(int rigId, [FromQuery] long? revision, CancellationToken cancellationToken)
        => await ExecuteAsync(() => _opticsService.DeleteRigAsync(rigId, revision, cancellationToken)).ConfigureAwait(false);

    private async Task<ActionResult<OpticsCatalogResponse>> ExecuteAsync(Func<Task<HVO.Result<OpticsCatalogResponse>>> operation)
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
