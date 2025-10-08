#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Provides a baseline implementation for camera adapters, handling descriptor plumbing,
/// lifecycle guards, and logging while delegating capture specifics to subclasses.
/// </summary>
public abstract class CameraAdapterBase : ICameraAdapter
{
    private bool _initialized;

    protected CameraAdapterBase(
        RigSpec rig,
        ILogger? logger = null)
    {
        Rig = rig ?? throw new ArgumentNullException(nameof(rig));
        if (Rig.Descriptor is null)
        {
            throw new ArgumentException("Rig specification must include a camera descriptor.", nameof(rig));
        }
        Logger = logger ?? NullLogger.Instance;
    }

    public RigSpec Rig { get; }

    protected ILogger Logger { get; }

    protected sealed record AdapterFrame(
        SKBitmap Bitmap,
        StarFieldEngine Engine,
        DateTimeOffset Timestamp,
        double LatitudeDeg,
        double LongitudeDeg,
        bool FlipHorizontal,
        double? HorizonPadding,
        bool ApplyRefraction,
        ExposureSettings Exposure,
        int? StarCount = null,
        int? PlanetCount = null,
        Action<FrameContext>? DisposeAction = null);

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task<Result<bool>> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_initialized)
        {
            Logger.LogDebug("Camera adapter {Adapter} already initialized.", GetType().Name);
            return Result<bool>.Success(true);
        }

        var result = await OnInitializeAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result;
        }

        _initialized = true;
        Logger.LogInformation("Camera adapter {Adapter} initialized for rig {Rig}.", GetType().Name, Rig.Name);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_initialized)
        {
            Logger.LogDebug("Camera adapter {Adapter} shutdown requested while not initialized.", GetType().Name);
            return Result<bool>.Success(true);
        }

        var result = await OnShutdownAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result;
        }

        _initialized = false;
        Logger.LogInformation("Camera adapter {Adapter} shutdown complete.", GetType().Name);
        return Result<bool>.Success(true);
    }

    public async Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            var error = new InvalidOperationException("Camera adapter has not been initialized.");
            Logger.LogError(error, "Capture requested before initialization for adapter {Adapter}.", GetType().Name);
            return Result<CapturedImage>.Failure(error);
        }

        cancellationToken.ThrowIfCancellationRequested();

        AdapterFrame? frame = null;

        try
        {
            var frameResult = await CaptureFrameAsync(exposure, cancellationToken).ConfigureAwait(false);

            if (frameResult.IsFailure)
            {
                return Result<CapturedImage>.Failure(frameResult.Error ?? new InvalidOperationException("Frame capture failed."));
            }

            frame = frameResult.Value;

            var frameContext = new FrameContext(
                Rig,
                frame.Engine,
                frame.Timestamp,
                frame.LatitudeDeg,
                frame.LongitudeDeg,
                frame.FlipHorizontal,
                frame.HorizonPadding,
                frame.ApplyRefraction,
                frame.DisposeAction);

            var capturedImage = new CapturedImage(
                frame.Bitmap,
                frame.Timestamp,
                frame.Exposure,
                frameContext);

            OnFrameCaptured(frame, capturedImage);

            // Ownership transferred to CapturedImage/FrameContext.
            frame = null;

            return Result<CapturedImage>.Success(capturedImage);
        }
        catch (OperationCanceledException ex)
        {
            if (frame is not null)
            {
                DisposeFrame(frame);
                frame = null;
            }

            Logger.LogDebug(ex, "Capture cancelled for adapter {Adapter}.", GetType().Name);
            return Result<CapturedImage>.Failure(ex);
        }
        catch (Exception ex)
        {
            if (frame is not null)
            {
                DisposeFrame(frame);
                frame = null;
            }

            Logger.LogError(ex, "Capture failed for adapter {Adapter}.", GetType().Name);
            return Result<CapturedImage>.Failure(ex);
        }
        finally
        {
            if (frame is not null)
            {
                DisposeFrame(frame);
            }
        }
    }

    protected virtual Task<Result<bool>> OnInitializeAsync(CancellationToken cancellationToken)
        => Task.FromResult(Result<bool>.Success(true));

    protected virtual Task<Result<bool>> OnShutdownAsync(CancellationToken cancellationToken)
        => Task.FromResult(Result<bool>.Success(true));

    protected virtual void OnFrameCaptured(AdapterFrame frame, CapturedImage capturedImage)
    {
        if (frame.StarCount is int stars && frame.PlanetCount is int planets)
        {
            Logger.LogTrace(
                "Adapter {Adapter} captured frame at {TimestampUtc} with exposure {ExposureMs} ms, gain {Gain}, stars {StarCount}, planets {PlanetCount}.",
                GetType().Name,
                capturedImage.Timestamp.UtcDateTime,
                capturedImage.Exposure.ExposureMilliseconds,
                capturedImage.Exposure.Gain,
                stars,
                planets);
            return;
        }

        Logger.LogTrace(
            "Adapter {Adapter} captured frame at {TimestampUtc} with exposure {ExposureMs} ms, gain {Gain}.",
            GetType().Name,
            capturedImage.Timestamp.UtcDateTime,
            capturedImage.Exposure.ExposureMilliseconds,
            capturedImage.Exposure.Gain);
    }

    protected abstract Task<Result<AdapterFrame>> CaptureFrameAsync(ExposureSettings exposure, CancellationToken cancellationToken);

    private static void DisposeFrame(AdapterFrame frame)
    {
        try
        {
            frame.Bitmap.Dispose();
        }
        catch
        {
            // Ignore dispose failures
        }

        try
        {
            frame.Engine.Dispose();
        }
        catch
        {
            // Ignore dispose failures
        }
    }
}
