using System.Net.Mime;
using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/all-sky")]
public sealed class AllSkyController : ControllerBase
{
    private const string RawImageContentType = "image/png";

    private readonly IFrameStateStore _frameStateStore;
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private readonly ILogger<AllSkyController> _logger;

    public AllSkyController(
        IFrameStateStore frameStateStore,
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        ILogger<AllSkyController> logger)
    {
        _frameStateStore = frameStateStore;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(AllSkyStatusResponse), StatusCodes.Status200OK)]
    public ActionResult<AllSkyStatusResponse> GetStatus()
    {
        var status = _frameStateStore.GetStatus();
        return Ok(status);
    }

    [HttpGet("frame/latest")]
    [Produces(MediaTypeNames.Image.Jpeg)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLatestFrame([FromQuery] bool raw = false)
    {
        if (raw)
        {
            var frame = _frameStateStore.LatestRawFrame;
            if (frame is null)
            {
                return NotFound();
            }

            using var image = SKImage.FromBitmap(frame.Image);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return File(data.ToArray(), RawImageContentType);
        }
        else
        {
            var processed = _frameStateStore.LatestProcessedFrame;
            if (processed is null)
            {
                return NotFound();
            }

            return File(processed.ImageBytes, processed.ContentType);
        }
    }

    [HttpPost("configuration")]
    [ProducesResponseType(typeof(CameraConfiguration), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<CameraConfiguration> UpdateConfiguration([FromBody] UpdateCameraConfigurationRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var currentConfiguration = _frameStateStore.Configuration;
        var updatedConfiguration = currentConfiguration.WithUpdates(request);

        if (!TryValidateConfiguration(updatedConfiguration, out var validationProblem))
        {
            if (validationProblem is null)
            {
                return Problem("Configuration validation failed.");
            }

            return validationProblem;
        }

        _frameStateStore.UpdateConfiguration(updatedConfiguration);
        _logger.LogInformation("Camera configuration updated via API. EnableStacking:{EnableStacking} StackingFrameCount:{StackCount} Overlays:{Overlays} CircularApertureMask:{Mask} Filters:{Filters} ProcessedFormat:{Format} ProcessedQuality:{Quality}",
            updatedConfiguration.EnableStacking,
            updatedConfiguration.StackingFrameCount,
            updatedConfiguration.EnableImageOverlays,
            updatedConfiguration.EnableCircularApertureMask,
            string.Join(",", updatedConfiguration.FrameFilters),
            updatedConfiguration.ProcessedImageEncoding.Format,
            updatedConfiguration.ProcessedImageEncoding.Quality);

        return Ok(updatedConfiguration);
    }

    [HttpGet("configuration")]
    [ProducesResponseType(typeof(CameraConfiguration), StatusCodes.Status200OK)]
    public ActionResult<CameraConfiguration> GetConfiguration()
    {
        return Ok(_frameStateStore.Configuration);
    }

    private bool TryValidateConfiguration(CameraConfiguration configuration, out ActionResult? problemDetails)
    {
        var options = _optionsMonitor.CurrentValue;

        if (configuration.StackingFrameCount < 1 || configuration.StackingFrameCount > 64)
        {
            problemDetails = BadRequest(new ProblemDetails
            {
                Title = "Invalid stacking frame count",
                Detail = "StackingFrameCount must be between 1 and 64."
            });
            return false;
        }

        if (configuration.EnableStacking && configuration.StackingFrameCount > options.StackingFrameCount * 4)
        {
            problemDetails = BadRequest(new ProblemDetails
            {
                Title = "Stacking frame count too large",
                Detail = $"StackingFrameCount must not exceed {options.StackingFrameCount * 4} when stacking is enabled."
            });
            return false;
        }

        problemDetails = null;
        return true;
    }
}
