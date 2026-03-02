using System;
using HVO.Core.Results;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using HVO.SkyMonitorV5.RPi.Models.ImageHistory;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Controllers.v1_0;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/history")]
public sealed class ImageHistoryController : ControllerBase
{
    private readonly IImageHistoryService _imageHistoryService;
    private readonly IFrameMediaProvider _frameMediaProvider;
    private readonly ILogger<ImageHistoryController> _logger;

    public ImageHistoryController(
        IImageHistoryService imageHistoryService,
        IFrameMediaProvider frameMediaProvider,
        ILogger<ImageHistoryController> logger)
    {
        _imageHistoryService = imageHistoryService ?? throw new ArgumentNullException(nameof(imageHistoryService));
        _frameMediaProvider = frameMediaProvider ?? throw new ArgumentNullException(nameof(frameMediaProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet("thumbnails")]
    [ProducesResponseType(typeof(ImageHistoryThumbnailPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ImageHistoryThumbnailPage>> GetThumbnailsAsync(
        [FromQuery] DateTimeOffset? since = null,
        [FromQuery] DateTimeOffset? until = null,
        [FromQuery] string? rig = null,
        [FromQuery] string? camera = null,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ImageHistoryThumbnailsRequest(since, until, rig, camera, pageSize, cursor);
        var result = await _imageHistoryService.GetThumbnailsAsync(request, cancellationToken).ConfigureAwait(false);

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
                title: "Invalid image history thumbnail request.",
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Problem(
            title: "Unable to load image history thumbnails.",
            detail: error?.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }

    [HttpGet("frames/{frameId:guid}")]
    [ProducesResponseType(typeof(ImageHistoryFrameDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ImageHistoryFrameDetail>> GetFrameAsync(Guid frameId, CancellationToken cancellationToken)
    {
        var result = await _imageHistoryService.GetFrameAsync(frameId, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccessful)
        {
            return Ok(result.Value.Detail);
        }

        var error = result.Error;
        if (error is OperationCanceledException)
        {
            throw error;
        }

        if (error is InvalidOperationException)
        {
            return NotFound();
        }

        return Problem(
            title: "Unable to load image history frame detail.",
            detail: error?.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }

    [HttpGet("frames/{frameId:guid}/media")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFrameMediaAsync(
        Guid frameId,
        [FromQuery(Name = "variant")] string variant = "processed",
        [FromQuery(Name = "rawFormat")] string? rawFormat = null,
        CancellationToken cancellationToken = default)
    {
        var frameResult = await _imageHistoryService.GetFrameAsync(frameId, cancellationToken).ConfigureAwait(false);
        if (!frameResult.IsSuccessful)
        {
            var error = frameResult.Error;
            if (error is OperationCanceledException)
            {
                throw error;
            }

            if (error is InvalidOperationException)
            {
                return NotFound();
            }

            return Problem(
                title: "Unable to resolve frame metadata for media retrieval.",
                detail: error?.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var detailResult = frameResult.Value;
        var detail = detailResult.Detail;
        var media = detailResult.Media;

        switch (variant?.Trim().ToLowerInvariant())
        {
            case "processed":
            case null:
            {
                return await DeliverProcessedFrameAsync(detail, cancellationToken).ConfigureAwait(false);
            }

            case "raw":
            {
                return await DeliverRawFrameAsync(detail, rawFormat, cancellationToken).ConfigureAwait(false);
            }

            case "thumbnail":
            {
                return await DeliverThumbnailAsync(detail, media, cancellationToken).ConfigureAwait(false);
            }

            default:
            {
                return Problem(
                    title: "Unsupported media variant requested.",
                    detail: "Variant must be one of processed, raw, or thumbnail.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }
    }

    [HttpGet("stats")]
    [ProducesResponseType(typeof(ImageHistoryStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ImageHistoryStatsResponse>> GetStatsAsync(
        [FromQuery] DateTimeOffset? since = null,
        [FromQuery] DateTimeOffset? until = null,
        [FromQuery] string? rig = null,
        [FromQuery] string? camera = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ImageHistoryStatsRequest(since, until, rig, camera);
        var result = await _imageHistoryService.GetStatsAsync(request, cancellationToken).ConfigureAwait(false);

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
                title: "Invalid image history stats request.",
                detail: error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Problem(
            title: "Unable to compute image history statistics.",
            detail: error?.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }

    private async Task<IActionResult> DeliverProcessedFrameAsync(ImageHistoryFrameDetail detail, CancellationToken cancellationToken)
    {
        var media = await _frameMediaProvider
            .GetProcessedFrameAsync(detail.FrameId, detail.CapturedAtUtc, cancellationToken)
            .ConfigureAwait(false);

        if (media is null)
        {
            _logger.LogInformation("Processed media for frame {FrameId} was not found in the archive or cache.", detail.FrameId);
            return NotFound();
        }

        var fileName = media.BuildDownloadFileName("processed-frame");
        return File(media.Payload, media.ContentType, fileName);
    }

    private async Task<IActionResult> DeliverRawFrameAsync(ImageHistoryFrameDetail detail, string? rawFormat, CancellationToken cancellationToken)
    {
        if (!detail.RawMediaAvailable)
        {
            _logger.LogInformation("Raw media payload for frame {FrameId} is not available in the archive. Falling back to live buffer if possible.", detail.FrameId);
        }

        if (!TryResolveRawFormat(rawFormat, out var format, out var formatError))
        {
            return Problem(
                title: "Invalid raw media format requested.",
                detail: formatError,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var media = await _frameMediaProvider
            .GetRawFrameAsync(detail.FrameId, detail.CapturedAtUtc, format, cancellationToken)
            .ConfigureAwait(false);

        if (media is null)
        {
            _logger.LogInformation("Raw media for frame {FrameId} could not be located for format {RawFormat}.", detail.FrameId, format);
            return NotFound();
        }

        var prefix = format == RawFrameMediaFormat.Png ? "raw-frame" : "raw-frame-native";
        var fileName = media.BuildDownloadFileName(prefix);
        return File(media.Payload, media.ContentType, fileName);
    }

    private Task<IActionResult> DeliverThumbnailAsync(ImageHistoryFrameDetail detail, ImageHistoryMediaReferences media, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(media.ThumbnailFilePath))
        {
            if (!string.IsNullOrWhiteSpace(media.ThumbnailObjectKey))
            {
                _logger.LogWarning("Thumbnail for frame {FrameId} resides in object storage ({Bucket}/{Key}) which is not yet implemented for retrieval.", detail.FrameId, media.ThumbnailBucket, media.ThumbnailObjectKey);
            }

            return Task.FromResult<IActionResult>(NotFound());
        }

        if (!System.IO.File.Exists(media.ThumbnailFilePath))
        {
            _logger.LogInformation("Thumbnail file for frame {FrameId} is missing at path {Path}.", detail.FrameId, media.ThumbnailFilePath);
            return Task.FromResult<IActionResult>(NotFound());
        }

        var stream = new FileStream(media.ThumbnailFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileName = FormattableString.Invariant($"thumbnail-{detail.CapturedAtUtc:yyyyMMdd-HHmmss}.jpg");
        return Task.FromResult<IActionResult>(File(stream, "image/jpeg", fileName));
    }

    private static bool TryResolveRawFormat(string? rawFormat, out RawFrameMediaFormat format, out string? error)
    {
        if (string.IsNullOrWhiteSpace(rawFormat))
        {
            format = RawFrameMediaFormat.Png;
            error = null;
            return true;
        }

        var token = rawFormat.Trim();
        if (string.Equals(token, "png", StringComparison.OrdinalIgnoreCase))
        {
            format = RawFrameMediaFormat.Png;
            error = null;
            return true;
        }

        if (string.Equals(token, "native", StringComparison.OrdinalIgnoreCase))
        {
            format = RawFrameMediaFormat.Native;
            error = null;
            return true;
        }

        format = RawFrameMediaFormat.Png;
        error = string.Create(CultureInfo.InvariantCulture, $"Unsupported raw format '{token}'. Expected png or native.");
        return false;
    }
}
