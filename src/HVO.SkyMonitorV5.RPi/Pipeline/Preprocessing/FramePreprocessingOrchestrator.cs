#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Skia;
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

    public FramePreprocessingOrchestrator(
        SkiaSurfacePool surfacePool,
        ILogger<FramePreprocessingOrchestrator> logger)
    {
        _surfacePool = surfacePool ?? throw new ArgumentNullException(nameof(surfacePool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            ApplyCalibrations(surface, frame);

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

    private static void ApplyCalibrations(SKSurface surface, CameraAdapterBase.AdapterFrame frame)
    {
        // Placeholder for future calibration passes (dark subtraction, flat-field correction, demosaic, etc.).
        // These operations will operate directly on the pooled surface once implemented.
        _ = surface;
        _ = frame;
    }
}
