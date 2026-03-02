#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;
using HVO.SkyMonitorV5.RPi.Skia;
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
    private readonly IObservatoryClock? _observatoryClock;
    private readonly IFramePreprocessingOrchestrator? _preprocessingOrchestrator;

    protected CameraAdapterBase(
        RigSpec rig,
        IObservatoryClock? observatoryClock = null,
        ILogger? logger = null,
        IFramePreprocessingOrchestrator? preprocessingOrchestrator = null)
    {
        Rig = rig ?? throw new ArgumentNullException(nameof(rig));
        _observatoryClock = observatoryClock;
        Logger = logger ?? NullLogger.Instance;
        _preprocessingOrchestrator = preprocessingOrchestrator;
    }

    public RigSpec Rig { get; }

    protected ILogger Logger { get; }

    /// <summary>
    /// Internal transport payload passed between pipeline stages. Subclasses can attach
    /// additional context via optional fields or the <see cref="DisposeAction"/> callback.
    /// </summary>
    public sealed record AdapterFrame(
        SKBitmap Bitmap,
        SkiaPixelLease? PixelLease,
        SKImage? ImmutableImage,
        SKSurface? Surface,
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

    /// <summary>
    /// Executes the canonical capture pipeline: exposure negotiation, raw frame acquisition,
    /// preprocessing, optional post-processing/stacking, metadata assembly, and final payload creation.
    /// </summary>
    /// <remarks>
    /// <para>Prior: <see cref="InitializeAsync"/> has completed and the capture service has negotiated the next exposure.</para>
    /// <para>Current: This method orchestrates the adapter pipeline – the call sequence below moves the frame through
    /// exposure configuration, acquisition, preprocessing, postprocessing, context creation, and final payload assembly.</para>
    /// <para>Next: The returned <see cref="CapturedImage"/> feeds the capture service export path (remote dispatch,
    /// telemetry, and exporters) before the loop prepares the subsequent exposure.</para>
    /// <para>The method runs entirely asynchronously. Long-running hardware interactions (for example sensor readout)
    /// should be implemented inside <see cref="AcquireImageAsync"/> and may block until the hardware completes exposure.</para>
    /// <para>Each stage returns an updated <see cref="AdapterFrame"/>. Override points allow adapters to perform
    /// calibration, stacking, or filter work before the image leaves the adapter. Downstream distribution (for example
    /// enqueueing to S3/FS writers or notifying external processors) should occur in <see cref="OnFrameCaptured"/>,
    /// which is called after the final <see cref="CapturedImage"/> is created; that hook can start asynchronous
    /// fan-out without blocking this pipeline.</para>
    /// </remarks>
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
            var exposureResult = await ConfigureExposureAsync(exposure, cancellationToken).ConfigureAwait(false);
            if (exposureResult.IsFailure)
            {
                return Result<CapturedImage>.Failure(exposureResult.Error ?? new InvalidOperationException("Exposure configuration failed."));
            }

            var effectiveExposure = exposureResult.Value;

            var frameResult = await AcquireImageAsync(effectiveExposure, cancellationToken).ConfigureAwait(false);
            if (frameResult.IsFailure)
            {
                return Result<CapturedImage>.Failure(frameResult.Error ?? new InvalidOperationException("Frame capture failed."));
            }

            frame = frameResult.Value;

            var preProcessResult = await PreprocessFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            if (preProcessResult.IsFailure)
            {
                return Result<CapturedImage>.Failure(preProcessResult.Error ?? new InvalidOperationException("Frame preprocessing failed."));
            }

            frame = preProcessResult.Value;

            var postProcessResult = await PostprocessFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            if (postProcessResult.IsFailure)
            {
                return Result<CapturedImage>.Failure(postProcessResult.Error ?? new InvalidOperationException("Frame postprocessing failed."));
            }

            frame = postProcessResult.Value;

            frame = EnsureImmutableImage(frame);

            var frameId = Guid.CreateVersion7();

            var frameContextResult = await CreateFrameContextAsync(frame, frameId, cancellationToken).ConfigureAwait(false);
            if (frameContextResult.IsFailure)
            {
                return Result<CapturedImage>.Failure(frameContextResult.Error ?? new InvalidOperationException("Frame context creation failed."));
            }

            var frameContext = frameContextResult.Value;

            var capturedResult = await CreateCapturedImageAsync(frame, frameId, effectiveExposure, frameContext, cancellationToken).ConfigureAwait(false);
            if (capturedResult.IsFailure)
            {
                return Result<CapturedImage>.Failure(capturedResult.Error ?? new InvalidOperationException("Frame assembly failed."));
            }

            var capturedImage = capturedResult.Value;

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

    /// <summary>
    /// Allows adapters to adjust the requested exposure before it is applied to the hardware. Typical use cases are
    /// enforcing device limits, tweaking gain/offset, or aligning exposure length with sensor timing constraints.
    /// </summary>
    /// <remarks>
    /// This method should complete quickly; it is executed before any hardware interaction and is expected to be CPU-bound.
    /// </remarks>
    protected virtual Task<Result<ExposureSettings>> ConfigureExposureAsync(ExposureSettings requestedExposure, CancellationToken cancellationToken)
        => Task.FromResult(Result<ExposureSettings>.Success(requestedExposure));

    /// <summary>
    /// Performs the hardware or simulator call that captures the raw bitmap. Implementations typically block until the
    /// sensor finishes the exposure, so they may run for the duration of the requested integration time.
    /// </summary>
    protected abstract Task<Result<AdapterFrame>> AcquireImageAsync(ExposureSettings exposure, CancellationToken cancellationToken);

    /// <summary>
    /// Gives adapters an opportunity to perform sensor-specific preprocessing (for example dark frame subtraction,
    /// hot pixel masking, or building an intermediate stacking buffer) before the frame moves further down the pipeline.
    /// </summary>
    /// <remarks>
    /// The default implementation is a pass-through. Override when the work is CPU-bound and safe to run inline; if heavy I/O
    /// or asynchronous fan-out is required, prefer queueing that work and returning promptly.
    /// </remarks>
    protected virtual Task<Result<AdapterFrame>> PreprocessFrameAsync(AdapterFrame frame, CancellationToken cancellationToken)
    {
        if (_preprocessingOrchestrator is null)
        {
            return Task.FromResult(Result<AdapterFrame>.Success(frame));
        }

        return _preprocessingOrchestrator.ProcessAsync(frame, cancellationToken);
    }

    /// <summary>
    /// Finalizes image adjustments before packaging. This is the common hook for stacking contributions, filter application,
    /// or tone mapping. Implementations can replace the bitmap carried inside the <see cref="AdapterFrame"/>.
    /// </summary>
    /// <remarks>
    /// Use this step for GPU/CPU intensive operations that remain synchronous. If an adapter needs to hand off to
    /// asynchronous processing (for example a background stacker), it should enqueue work here and return the best
    /// available interim frame so capture cadence is not blocked.
    /// </remarks>
    protected virtual Task<Result<AdapterFrame>> PostprocessFrameAsync(AdapterFrame frame, CancellationToken cancellationToken)
        => Task.FromResult(Result<AdapterFrame>.Success(frame));

    /// <summary>
    /// Creates the logical frame context that downstream services use to understand how the image was produced. This is the
    /// right place to attach information required by storage writers or external processors that consume queued work items.
    /// </summary>
    protected virtual Task<Result<FrameContext>> CreateFrameContextAsync(AdapterFrame frame, Guid frameId, CancellationToken cancellationToken)
    {
        var context = new FrameContext(
            frameId,
            Rig,
            frame.Engine,
            frame.Timestamp,
            frame.LatitudeDeg,
            frame.LongitudeDeg,
            frame.FlipHorizontal,
            frame.HorizonPadding,
            frame.ApplyRefraction,
            frame.DisposeAction);

        return Task.FromResult(Result<FrameContext>.Success(context));
    }

    /// <summary>
    /// Builds the final <see cref="CapturedImage"/> to be emitted. Override when the adapter must persist auxiliary data
    /// (for example raw FITS buffers) or when it needs to schedule uploads before returning control to the caller.
    /// </summary>
    protected virtual Task<Result<CapturedImage>> CreateCapturedImageAsync(AdapterFrame frame, Guid frameId, ExposureSettings exposure, FrameContext frameContext, CancellationToken cancellationToken)
    {
        var capturedImage = new CapturedImage(frameId, frame.Bitmap, frame.Timestamp, exposure, frameContext)
        {
            ImmutableImage = frame.ImmutableImage,
            PixelLease = frame.PixelLease
        };
        return Task.FromResult(Result<CapturedImage>.Success(capturedImage));
    }

    /// <summary>
    /// Notifies subclasses that the capture pipeline completed. Long-running fan-out should generally be handled by
    /// enqueueing work (for example to an S3/FS writer channel or external processing service) so that subsequent captures
    /// are not blocked; synchronous uploads should be avoided unless the adapter requires back-pressure.
    /// </summary>
    protected virtual void OnFrameCaptured(AdapterFrame frame, CapturedImage capturedImage)
    {
        if (frame.StarCount is int stars && frame.PlanetCount is int planets)
        {
            var timestampLocal = _observatoryClock?.ToLocal(capturedImage.Timestamp) ?? capturedImage.Timestamp.ToLocalTime();
            Logger.LogTrace(
                "Adapter {Adapter} captured frame at {TimestampLocal} with exposure {ExposureMs} ms, gain {Gain}, stars {StarCount}, planets {PlanetCount}.",
                GetType().Name,
                timestampLocal,
                capturedImage.Exposure.ExposureMilliseconds,
                capturedImage.Exposure.Gain,
                stars,
                planets);
            return;
        }

        var fallbackTimestampLocal = _observatoryClock?.ToLocal(capturedImage.Timestamp) ?? capturedImage.Timestamp.ToLocalTime();
        Logger.LogTrace(
            "Adapter {Adapter} captured frame at {TimestampLocal} with exposure {ExposureMs} ms, gain {Gain}.",
            GetType().Name,
            fallbackTimestampLocal,
            capturedImage.Exposure.ExposureMilliseconds,
            capturedImage.Exposure.Gain);
    }

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
            frame.PixelLease?.Dispose();
        }
        catch
        {
            // Ignore dispose failures
        }

        try
        {
            frame.ImmutableImage?.Dispose();
        }
        catch
        {
            // Ignore dispose failures
        }

        try
        {
            frame.Surface?.Dispose();
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

    private static AdapterFrame EnsureImmutableImage(AdapterFrame frame)
    {
        if (frame.ImmutableImage is not null)
        {
            return frame;
        }

        if (frame.Surface is SKSurface surface)
        {
            var snapshot = surface.Snapshot();
            surface.Dispose();
            return frame with { ImmutableImage = snapshot, Surface = null };
        }

        var fallback = SKImage.FromBitmap(frame.Bitmap);
        return frame with { ImmutableImage = fallback };
    }
}
