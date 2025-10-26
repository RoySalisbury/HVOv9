#nullable enable

using Asp.Versioning;
using HVO;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyCamera;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyImageSave;
using HVO.SkyMonitorV4.RPi.HostedServices.AllSkyTimelapse;
using HVO.SkyMonitorV4.RPi.Models.AllSky;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HVO.SkyMonitorV4.RPi.Controllers;

/// <summary>
/// Provides REST endpoints for interacting with the All-Sky camera service.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Produces("application/json")]
[Route("api/v{version:apiVersion}/all-sky")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
[Tags("All-Sky Camera")]
public sealed class AllSkyCameraController : ControllerBase
{
    private const string ImageContentType = "image/jpeg";

    private readonly IAllSkyCameraService _cameraService;
    private readonly IAllSkyTimelapseService _timelapseService;
    private readonly AllSkyImageSaveOptions _imageSaveOptions;
    private readonly AllSkyTimelapseOptions _timelapseOptions;
    private readonly ILogger<AllSkyCameraController> _logger;

    public AllSkyCameraController(
        IAllSkyCameraService cameraService,
        IAllSkyTimelapseService timelapseService,
        IOptions<AllSkyImageSaveOptions> imageSaveOptions,
        IOptions<AllSkyTimelapseOptions> timelapseOptions,
        ILogger<AllSkyCameraController> logger)
    {
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _timelapseService = timelapseService ?? throw new ArgumentNullException(nameof(timelapseService));
        _imageSaveOptions = imageSaveOptions?.Value ?? throw new ArgumentNullException(nameof(imageSaveOptions));
        _timelapseOptions = timelapseOptions?.Value ?? throw new ArgumentNullException(nameof(timelapseOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves the current status of the All-Sky camera service.
    /// </summary>
    [HttpGet("status", Name = nameof(GetStatus))]
    [ProducesResponseType(typeof(AllSkyStatusResponse), StatusCodes.Status200OK)]
    public ActionResult<AllSkyStatusResponse> GetStatus()
    {
        var result = Execute(CreateStatusResponse);
        return FromResult(result);
    }

    /// <summary>
    /// Streams the most recent All-Sky image if available.
    /// </summary>
    [HttpGet("latest-image", Name = nameof(GetLatestImage))]
    [Produces(ImageContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public IActionResult GetLatestImage()
    {
        var result = Execute(() =>
        {
            EnsureCameraRecording();
            return OpenLatestImage();
        });

        return result.Match<IActionResult>(
            success => CreateImageResult(success.Stream, success.LastModified),
            failure => MapException(failure));
    }

    /// <summary>
    /// Retrieves the current exposure settings for the All-Sky camera.
    /// </summary>
    [HttpGet("exposure", Name = nameof(GetExposureSettings))]
    [ProducesResponseType(typeof(AllSkyExposureResponse), StatusCodes.Status200OK)]
    public ActionResult<AllSkyExposureResponse> GetExposureSettings()
    {
        var result = Execute(CreateExposureResponse);
        return FromResult(result);
    }

    /// <summary>
    /// Updates the camera exposure brightness setting.
    /// </summary>
    [HttpPut("exposure/brightness", Name = nameof(UpdateExposureBrightness))]
    [ProducesResponseType(typeof(AllSkyExposureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllSkyExposureResponse> UpdateExposureBrightness([FromBody] UpdateIntValueRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = Execute(() =>
        {
            EnsureCameraRecording();
            _cameraService.ExposureBrightness = request.Value!.Value;
            return CreateExposureResponse();
        });

        return FromResult(result);
    }

    /// <summary>
    /// Updates whether the camera manages brightness automatically.
    /// </summary>
    [HttpPut("exposure/brightness/auto", Name = nameof(UpdateAutoBrightness))]
    [ProducesResponseType(typeof(AllSkyExposureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllSkyExposureResponse> UpdateAutoBrightness([FromBody] UpdateBooleanRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = Execute(() =>
        {
            EnsureCameraRecording();
            _cameraService.AutoExposureBrightness = request.Value!.Value;
            return CreateExposureResponse();
        });

        return FromResult(result);
    }

    /// <summary>
    /// Updates the camera exposure gain.
    /// </summary>
    [HttpPut("exposure/gain", Name = nameof(UpdateExposureGain))]
    [ProducesResponseType(typeof(AllSkyExposureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllSkyExposureResponse> UpdateExposureGain([FromBody] UpdateIntValueRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = Execute(() =>
        {
            EnsureCameraRecording();
            _cameraService.ExposureGain = request.Value!.Value;
            return CreateExposureResponse();
        });

        return FromResult(result);
    }

    /// <summary>
    /// Updates whether the camera manages gain automatically.
    /// </summary>
    [HttpPut("exposure/gain/auto", Name = nameof(UpdateAutoGain))]
    [ProducesResponseType(typeof(AllSkyExposureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllSkyExposureResponse> UpdateAutoGain([FromBody] UpdateBooleanRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = Execute(() =>
        {
            EnsureCameraRecording();
            _cameraService.AutoExposureGain = request.Value!.Value;
            return CreateExposureResponse();
        });

        return FromResult(result);
    }

    /// <summary>
    /// Updates the camera exposure duration in milliseconds.
    /// </summary>
    [HttpPut("exposure/duration", Name = nameof(UpdateExposureDuration))]
    [ProducesResponseType(typeof(AllSkyExposureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllSkyExposureResponse> UpdateExposureDuration([FromBody] UpdateIntValueRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = Execute(() =>
        {
            EnsureCameraRecording();
            _cameraService.ExposureDurationMilliseconds = request.Value!.Value;
            return CreateExposureResponse();
        });

        return FromResult(result);
    }

    /// <summary>
    /// Updates whether the camera manages exposure duration automatically.
    /// </summary>
    [HttpPut("exposure/duration/auto", Name = nameof(UpdateAutoExposureDuration))]
    [ProducesResponseType(typeof(AllSkyExposureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllSkyExposureResponse> UpdateAutoExposureDuration([FromBody] UpdateBooleanRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = Execute(() =>
        {
            EnsureCameraRecording();
            _cameraService.AutoExposureDuration = request.Value!.Value;
            return CreateExposureResponse();
        });

        return FromResult(result);
    }

    /// <summary>
    /// Updates the image circle rotation angle in degrees.
    /// </summary>
    [HttpPut("image-circle/rotation", Name = nameof(UpdateImageCircleRotation))]
    [ProducesResponseType(typeof(AllSkyStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<AllSkyStatusResponse> UpdateImageCircleRotation([FromBody] UpdateDoubleValueRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = Execute(() =>
        {
            _cameraService.ImageCircleRotationAngle = request.Value!.Value;
            return CreateStatusResponse();
        });

        return FromResult(result);
    }

    /// <summary>
    /// Queues a timelapse generation request for the specified time window.
    /// </summary>
    [HttpPost("timelapse", Name = nameof(RequestTimelapseGeneration))]
    [ProducesResponseType(typeof(AllSkyTimelapseQueuedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult RequestTimelapseGeneration([FromBody] CreateTimelapseRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = Execute(() =>
        {
            var start = request.StartTimeUtc!.Value;
            var end = start.AddSeconds(request.DurationSeconds);
            var prefix = string.IsNullOrWhiteSpace(request.OutputPrefix) ? _timelapseOptions.OutputPrefix : request.OutputPrefix!.Trim();

            _ = Task.Run(async () =>
            {
                try
                {
                    var creationResult = await _timelapseService.CreateTimelapseAsync(start, end, prefix, CancellationToken.None).ConfigureAwait(false);
                    if (creationResult.IsSuccessful)
                    {
                        _logger.LogInformation("Timelapse queued request completed successfully for interval {Start} - {End}. Output: {OutputPath}", start, end, creationResult.Value);
                    }
                    else if (creationResult.Error is { } error && error is not OperationCanceledException)
                    {
                        _logger.LogError(error, "Timelapse generation failed for interval {Start} - {End}.", start, end);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Timelapse generation failed for interval {Start} - {End}.", start, end);
                }
            });

            return new AllSkyTimelapseQueuedResponse(start, end, prefix);
        });

        return result.Match<IActionResult>(Accepted, MapException);
    }

    private AllSkyStatusResponse CreateStatusResponse()
    {
        var lastImageTimestamp = _cameraService.LastImageTakenTimestamp;
        var hasRecentImage = HasRecentImage(lastImageTimestamp);
        var relativePath = hasRecentImage ? _cameraService.LastImageRelativePath : null;

        return new AllSkyStatusResponse(
            _cameraService.IsRecording,
            hasRecentImage ? lastImageTimestamp : null,
            hasRecentImage,
            relativePath,
            _cameraService.MaxAttemptedFps,
            _cameraService.ImageCircleRotationAngle,
            CreateExposureResponse());
    }

    private AllSkyExposureResponse CreateExposureResponse()
    {
        return new AllSkyExposureResponse(
            _cameraService.ExposureBrightness,
            _cameraService.AutoExposureBrightness,
            _cameraService.AutoExposureBrightnessTarget,
            _cameraService.ExposureGain,
            _cameraService.AutoExposureGain,
            _cameraService.AutoExposureMaxGain,
            _cameraService.ExposureDurationMilliseconds,
            _cameraService.AutoExposureDuration,
            _cameraService.AutoExposureMaxDurationMilliseconds);
    }

    private bool HasRecentImage(DateTimeOffset timestamp)
    {
        if (timestamp == DateTimeOffset.MinValue)
        {
            return false;
        }

        var maxAgeSeconds = Math.Max(1, _imageSaveOptions.MaxImageAgeSeconds);
        var maxAge = TimeSpan.FromSeconds(maxAgeSeconds);
        return (DateTimeOffset.UtcNow - timestamp) <= maxAge;
    }

    private void EnsureCameraRecording()
    {
        if (_cameraService.IsRecording)
        {
            return;
        }

        throw new InvalidOperationException("The All-Sky camera is not currently recording.");
    }

    private (FileStream Stream, DateTimeOffset LastModified) OpenLatestImage()
    {
        var relativePath = _cameraService.LastImageRelativePath;
        var timestamp = _cameraService.LastImageTakenTimestamp;

        if (!HasRecentImage(timestamp) || string.IsNullOrWhiteSpace(relativePath))
        {
            throw new FileNotFoundException("A recent All-Sky image is not available.");
        }

        var absolutePath = Path.Combine(_cameraService.ImageCacheRoot, relativePath);
        var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream, timestamp);
    }

    private Result<T> Execute<T>(Func<T> action)
    {
        try
        {
            return Result<T>.Success(action());
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(ex);
        }
    }

    private ActionResult<TResponse> FromResult<TResponse>(Result<TResponse> result)
    {
        return result.Match<ActionResult<TResponse>>(
            success => Ok(success),
            failure => MapException<TResponse>(failure));
    }

    private IActionResult MapException(Exception? exception)
    {
        return exception switch
        {
            InvalidOperationException invalidOperation => Problem(
                title: "Camera Inactive",
                detail: invalidOperation.Message,
                statusCode: StatusCodes.Status409Conflict),
            FileNotFoundException notFound => Problem(
                title: "Image Not Found",
                detail: notFound.Message,
                statusCode: StatusCodes.Status404NotFound),
            IOException ioException => Problem(
                title: "Image Access Error",
                detail: ioException.Message,
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Problem(
                title: "Internal Server Error",
                detail: exception?.Message ?? "An unexpected error occurred.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }

    private ActionResult<TResponse> MapException<TResponse>(Exception? exception)
    {
        var actionResult = MapException(exception);

        return actionResult switch
        {
            ObjectResult objectResult => objectResult,
            StatusCodeResult statusCodeResult => statusCodeResult,
            FileResult fileResult => fileResult,
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private FileStreamResult CreateImageResult(Stream stream, DateTimeOffset lastModified)
    {
        var result = File(stream, ImageContentType);
        result.LastModified = lastModified;
        result.EnableRangeProcessing = true;
        return result;
    }
}
