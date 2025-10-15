using System;
using System.Globalization;
using System.Net.Mime;
using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Storage;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/all-sky")]
public sealed class AllSkyController : ControllerBase
{
    private const string RawPngContentType = "image/png";

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
    public IActionResult GetLatestFrame([FromQuery] bool raw = false, [FromQuery(Name = "rawFormat")] string? rawFormat = null)
    {
        if (raw)
        {
            var frame = _frameStateStore.LatestRawFrame;
            if (frame is null)
            {
                return NotFound();
            }

            SKImage? temporary = null;
            try
            {
                var sourceImage = frame.ImmutableImage ?? (temporary = SKImage.FromBitmap(frame.Image));
                if (sourceImage is null)
                {
                    return NotFound();
                }

                var formatPreference = ResolveRawFrameFormat(rawFormat);

                var descriptorForHeaders = frame.ImageDescriptor;
                byte[]? rawPayload = null;
                FrameExportImageDescriptor? computedDescriptor = null;

                if (formatPreference != RawFrameFormatPreference.Png
                    && SkiaRawFrameHelper.TryCreateRawPayload(sourceImage, out var payload, out var extractedDescriptor))
                {
                    rawPayload = payload;
                    computedDescriptor = extractedDescriptor;
                }

                descriptorForHeaders ??= computedDescriptor ?? SkiaRawFrameHelper.TryCreateDescriptor(sourceImage);

                if (rawPayload is not null)
                {
                    if (descriptorForHeaders is not null)
                    {
                        WriteRawDescriptorHeaders(frame, descriptorForHeaders);
                    }

                    return File(rawPayload, SkiaRawFrameHelper.RawContentType);
                }

                if (formatPreference == RawFrameFormatPreference.Raw)
                {
                    _logger.LogWarning("Raw frame requested in raw format but payload extraction failed. Returning PNG fallback for FrameId {FrameId}.", frame.FrameId);
                }
                else if (formatPreference == RawFrameFormatPreference.Png)
                {
                    _logger.LogDebug("Raw frame requested with PNG format. Returning encoded payload for FrameId {FrameId}.", frame.FrameId);
                }
                else
                {
                    _logger.LogDebug("Raw frame payload unavailable. Returning PNG fallback for FrameId {FrameId}.", frame.FrameId);
                }

                using var data = sourceImage.Encode(SKEncodedImageFormat.Png, 90);
                if (descriptorForHeaders is not null)
                {
                    WriteRawDescriptorHeaders(frame, descriptorForHeaders);
                }

                return File(data.ToArray(), RawPngContentType);
            }
            finally
            {
                temporary?.Dispose();
            }
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

    private void WriteRawDescriptorHeaders(RawFrameSnapshot frame, FrameExportImageDescriptor descriptor)
    {
        var headers = Response.Headers;
        headers["X-HVO-Raw-FrameId"] = frame.FrameId.ToString("D", CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-TimestampUtc"] = frame.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-Width"] = descriptor.Width.ToString(CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-Height"] = descriptor.Height.ToString(CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-RowBytes"] = descriptor.RowBytes.ToString(CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-BytesPerPixel"] = descriptor.BytesPerPixel.ToString(CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-ColorType"] = descriptor.ColorType;
        headers["X-HVO-Raw-AlphaType"] = descriptor.AlphaType;
        headers["X-HVO-Raw-GammaLinear"] = descriptor.GammaIsLinear.ToString(CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-IsSrgb"] = descriptor.IsSrgb.ToString(CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-TransferNumeric"] = descriptor.HasNumericalTransferFunction.ToString(CultureInfo.InvariantCulture);
        headers["X-HVO-Raw-PixelFormat"] = descriptor.PixelFormatHint;

        if (!string.IsNullOrWhiteSpace(descriptor.ColorSpaceDescription))
        {
            headers["X-HVO-Raw-ColorSpace"] = descriptor.ColorSpaceDescription;
        }
    }

    private static RawFrameFormatPreference ResolveRawFrameFormat(string? rawFormat)
    {
        if (string.IsNullOrWhiteSpace(rawFormat))
        {
            return RawFrameFormatPreference.Auto;
        }

        if (rawFormat.Equals("png", StringComparison.OrdinalIgnoreCase)
            || rawFormat.Equals("image/png", StringComparison.OrdinalIgnoreCase))
        {
            return RawFrameFormatPreference.Png;
        }

        if (rawFormat.Equals("raw", StringComparison.OrdinalIgnoreCase)
            || rawFormat.Equals("skimg", StringComparison.OrdinalIgnoreCase)
            || rawFormat.Equals(SkiaRawFrameHelper.RawContentType, StringComparison.OrdinalIgnoreCase))
        {
            return RawFrameFormatPreference.Raw;
        }

        return RawFrameFormatPreference.Auto;
    }

    private enum RawFrameFormatPreference
    {
        Auto,
        Raw,
        Png
    }
}
