#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing.Calibration;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;

/// <summary>
/// Default implementation that maps captured frames onto pooled linear <see cref="SKSurface"/> instances so
/// demosaic and calibration passes can operate in-place before the frame reaches the stacking pipeline.
/// </summary>
internal sealed class FramePreprocessingOrchestrator : IFramePreprocessingOrchestrator
{
    private readonly SkiaSurfacePool _surfacePool;
    private readonly ILogger<FramePreprocessingOrchestrator> _logger;
    private readonly IFrameCalibrationPipelineFactory _pipelineFactory;

    public FramePreprocessingOrchestrator(
        SkiaSurfacePool surfacePool,
        ILogger<FramePreprocessingOrchestrator> logger,
        IFrameCalibrationPipelineFactory? pipelineFactory = null)
    {
        _surfacePool = surfacePool ?? throw new ArgumentNullException(nameof(surfacePool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipelineFactory = pipelineFactory ?? NullFrameCalibrationPipelineFactory.Instance;
    }

    public Task<Result<CameraAdapterBase.AdapterFrame>> ProcessAsync(CameraAdapterBase.AdapterFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // If the adapter already supplied a surface (mock pipeline), defer to that implementation.
        if (frame.Surface is not null)
        {
            return Task.FromResult(Result<CameraAdapterBase.AdapterFrame>.Success(frame));
        }

        SKImage? temporarySource = null;
        SKBitmap? processedBitmap = null;
        SkiaPixelLease? processedLease = null;
        var originalImage = frame.ImmutableImage;

        try
        {
            var sourceImage = originalImage;
            var disposeTemporarySource = false;

            if (sourceImage is null)
            {
                if (frame.PixelLease is not null)
                {
                    sourceImage = frame.PixelLease.Snapshot(copyPixels: true)
                        ?? throw new InvalidOperationException("Pixel lease snapshot failed during preprocessing.");
                    disposeTemporarySource = true;
                    temporarySource = sourceImage;
                }
                else
                {
                    sourceImage = SKImage.FromBitmap(frame.Bitmap)
                        ?? throw new InvalidOperationException("Unable to snapshot bitmap for preprocessing.");
                    disposeTemporarySource = true;
                    temporarySource = sourceImage;
                }
            }

            using var surfaceLease = _surfacePool.RentLinearSurface(sourceImage.Width, sourceImage.Height);
            var surface = surfaceLease.Surface;

            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(sourceImage, 0, 0);
            surface.Canvas.Flush();

            ApplyCalibrations(surfaceLease, frame, cancellationToken);

            var processedSurfaceSnapshot = surface.Snapshot()
                ?? throw new InvalidOperationException("Failed to snapshot preprocessing surface.");

            SKImage? processedImage = null;
            try
            {
                processedImage = SkiaImageUtilities.CloneToRaster(processedSurfaceSnapshot)
                    ?? throw new InvalidOperationException("Failed to produce raster snapshot from preprocessing surface.");
            }
            finally
            {
                processedSurfaceSnapshot.Dispose();
            }

            processedBitmap = SkiaImageUtilities.CreateBitmapCopy(processedImage);

            processedLease = SkiaPixelLease.FromBitmap(processedBitmap, disposeBitmap: false);

            frame.PixelLease?.Dispose();
            if (originalImage is not null)
            {
                originalImage.Dispose();
            }
            frame.Bitmap.Dispose();

            if (disposeTemporarySource && !ReferenceEquals(sourceImage, processedImage))
            {
                sourceImage.Dispose();
            }

            var updated = frame with
            {
                Bitmap = processedBitmap,
                PixelLease = processedLease,
                ImmutableImage = processedImage,
                Surface = null
            };

            // Ownership transferred to the frame; prevent disposal in finally block.
            processedBitmap = null;
            processedLease = null;
            temporarySource = null;

            return Task.FromResult(Result<CameraAdapterBase.AdapterFrame>.Success(updated));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Frame preprocessing failed.");
            return Task.FromResult(Result<CameraAdapterBase.AdapterFrame>.Failure(ex));
        }
        finally
        {
            temporarySource?.Dispose();
            processedLease?.Dispose();
            processedBitmap?.Dispose();
        }
    }

    private void ApplyCalibrations(SkiaSurfaceLease surfaceLease, CameraAdapterBase.AdapterFrame frame, CancellationToken cancellationToken)
    {
        var stages = _pipelineFactory.BuildStages();
        if (stages.Length == 0)
        {
            return;
        }

    var context = new FrameCalibrationContext(frame, surfaceLease);

        foreach (var stage in stages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var task = stage.ApplyAsync(context, cancellationToken);
                if (!task.IsCompletedSuccessfully)
                {
                    task.AsTask().GetAwaiter().GetResult();
                }
                else
                {
                    task.GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Calibration stage {StageName} failed; continuing with remaining stages.", stage.Name);
            }
        }
    }
}
