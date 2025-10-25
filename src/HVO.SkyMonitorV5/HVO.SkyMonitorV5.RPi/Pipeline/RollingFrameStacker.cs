#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Maintains a rolling buffer of frames and combines them using a color-preserving,
/// gamma-aware averaging strategy when stacking is enabled.
/// </summary>
public sealed class RollingFrameStacker : IFrameStacker, IFrameStackerConfigurationListener
{
    private sealed record BufferedFrame(SKImage Image, ExposureSettings Exposure, FrameMetadata? Metadata);

    private sealed record FrameMetadata(
        RigSpec Rig,
        double LatitudeDeg,
        double LongitudeDeg,
        bool FlipHorizontal,
        double? HorizonPadding,
        bool ApplyRefraction);

    private readonly Queue<BufferedFrame> _buffer = new();
    private int _bufferedIntegrationMilliseconds;
    private readonly ILogger<RollingFrameStacker>? _logger;
    private readonly SkiaSurfacePool _surfacePool;

    public RollingFrameStacker(SkiaSurfacePool surfacePool, ILogger<RollingFrameStacker>? logger = null)
    {
        _surfacePool = surfacePool ?? throw new ArgumentNullException(nameof(surfacePool));
        _logger = logger;
    }

    public FrameStackResult Accumulate(CapturedImage capture, CameraConfiguration configuration)
    {
        var frameContext = capture.Context;
        var frameMetadata = frameContext is not null ? CreateMetadata(frameContext) : null;

        EnsureBufferCompatibility(frameMetadata);

        if (!configuration.EnableStacking || configuration.StackingFrameCount <= 1)
        {
            DrainBuffer();
            var stackedImmutableSingle = SkiaImageUtilities.SnapshotToImmutable(capture.ImmutableImage, capture.Image);
            var originalImmutableSingle = SkiaImageUtilities.SnapshotToImmutable(capture.ImmutableImage, capture.Image);
            return new FrameStackResult(
                capture.FrameId,
                capture.Image,
                capture.Image,
                capture.Timestamp,
                capture.Exposure,
                frameContext,
                1,
                capture.Exposure.ExposureMilliseconds)
            {
                StackedImmutableImage = stackedImmutableSingle,
                OriginalImmutableImage = originalImmutableSingle
            };
        }

        var bufferedImage = SkiaImageUtilities.SnapshotToImmutable(capture.ImmutableImage, capture.Image)
            ?? throw new InvalidOperationException("Failed to create buffered immutable image snapshot.");
        _buffer.Enqueue(new BufferedFrame(bufferedImage, capture.Exposure, frameMetadata));
        _bufferedIntegrationMilliseconds += capture.Exposure.ExposureMilliseconds;

        TrimBuffer(configuration);

        try
        {
            var framesForStackCount = Math.Min(configuration.StackingFrameCount, _buffer.Count);
            if (framesForStackCount <= 0)
            {
                var stackedImmutableSingle = SkiaImageUtilities.SnapshotToImmutable(capture.ImmutableImage, capture.Image);
                var originalImmutableSingle = SkiaImageUtilities.SnapshotToImmutable(capture.ImmutableImage, capture.Image);
                return new FrameStackResult(
                    capture.FrameId,
                    capture.Image,
                    capture.Image,
                    capture.Timestamp,
                    capture.Exposure,
                    frameContext,
                    1,
                    capture.Exposure.ExposureMilliseconds)
                {
                    StackedImmutableImage = stackedImmutableSingle,
                    OriginalImmutableImage = originalImmutableSingle
                };
            }

            var framesForStack = GetFramesForStack(framesForStackCount);
            var stackStopwatch = Stopwatch.StartNew();
            var result = AverageFrames(framesForStack, capture, frameContext);
            stackStopwatch.Stop();

            if (_logger?.IsEnabled(LogLevel.Trace) == true && result.FramesStacked > 1)
            {
                var width = framesForStack.Count > 0 ? framesForStack[0].Image.Width : capture.Image.Width;
                var height = framesForStack.Count > 0 ? framesForStack[0].Image.Height : capture.Image.Height;
                _logger.LogTrace(
                    "Stacked {FramesStacked} frames (target {Target}, buffer {BufferCount}, integration {IntegrationMs}ms) in {DurationMs:F1}ms at {Width}x{Height}.",
                    result.FramesStacked,
                    configuration.StackingFrameCount,
                    _buffer.Count,
                    result.IntegrationMilliseconds,
                    stackStopwatch.Elapsed.TotalMilliseconds,
                    width,
                    height);
            }

            return result;
        }
        catch
        {
            var stackedImmutableSingle = SkiaImageUtilities.SnapshotToImmutable(capture.ImmutableImage, capture.Image);
            var originalImmutableSingle = SkiaImageUtilities.SnapshotToImmutable(capture.ImmutableImage, capture.Image);
            return new FrameStackResult(
                capture.FrameId,
                capture.Image,
                capture.Image,
                capture.Timestamp,
                capture.Exposure,
                frameContext,
                1,
                capture.Exposure.ExposureMilliseconds)
            {
                StackedImmutableImage = stackedImmutableSingle,
                OriginalImmutableImage = originalImmutableSingle
            };
        }
    }

    public void Reset() => DrainBuffer();

    public void OnConfigurationChanged(CameraConfiguration previousConfiguration, CameraConfiguration currentConfiguration)
    {
        if (currentConfiguration is null)
        {
            return;
        }

        if (!currentConfiguration.EnableStacking)
        {
            DrainBuffer();
            return;
        }

        if (!previousConfiguration?.EnableStacking ?? true)
        {
            return;
        }

        TrimBuffer(currentConfiguration);
    }

    private FrameStackResult AverageFrames(IReadOnlyList<BufferedFrame> frames, CapturedImage latestFrame, FrameContext? context)
    {
        if (frames.Count == 0)
        {
            var stackedImmutableSingle = SkiaImageUtilities.SnapshotToImmutable(latestFrame.ImmutableImage, latestFrame.Image);
            var originalImmutableSingle = SkiaImageUtilities.SnapshotToImmutable(latestFrame.ImmutableImage, latestFrame.Image);

            return new FrameStackResult(
                latestFrame.FrameId,
                latestFrame.Image,
                latestFrame.Image,
                latestFrame.Timestamp,
                latestFrame.Exposure,
                context,
                1,
                latestFrame.Exposure.ExposureMilliseconds)
            {
                StackedImmutableImage = stackedImmutableSingle,
                OriginalImmutableImage = originalImmutableSingle
            };
        }

        var referenceImage = frames[0].Image;
        var width = referenceImage.Width;
        var height = referenceImage.Height;

        if (width <= 0 || height <= 0)
        {
            return new FrameStackResult(
                latestFrame.FrameId,
                latestFrame.Image,
                latestFrame.Image,
                latestFrame.Timestamp,
                latestFrame.Exposure,
                context,
                1,
                latestFrame.Exposure.ExposureMilliseconds);
        }

        using var surfaceLease = _surfacePool.RentLinearSurface(width, height);
        var surface = surfaceLease.Surface;
        surface.Canvas.Clear(SKColors.Transparent);

        var framesIncluded = new List<BufferedFrame>(frames.Count);

        foreach (var frame in frames)
        {
            var image = frame.Image;
            if (image.Width != width || image.Height != height)
            {
                continue;
            }

            framesIncluded.Add(frame);
        }

        if (framesIncluded.Count == 0)
        {
            var fallbackImmutable = SkiaImageUtilities.SnapshotToImmutable(latestFrame.ImmutableImage, latestFrame.Image);
            return new FrameStackResult(
                latestFrame.FrameId,
                latestFrame.Image,
                latestFrame.Image,
                latestFrame.Timestamp,
                latestFrame.Exposure,
                context,
                1,
                latestFrame.Exposure.ExposureMilliseconds)
            {
                StackedImmutableImage = fallbackImmutable,
                OriginalImmutableImage = fallbackImmutable
            };
        }

        using var paint = CreateWeightedPaint(framesIncluded.Count);

        foreach (var buffered in framesIncluded)
        {
            surface.Canvas.DrawImage(buffered.Image, 0, 0, paint);
        }

        surface.Canvas.Flush();

        var stackedLinearImage = surface.Snapshot()
            ?? throw new InvalidOperationException("Failed to snapshot accumulated surface.");
        var stackedImmutable = SkiaImageUtilities.CloneToRaster(stackedLinearImage)
            ?? throw new InvalidOperationException("Failed to produce raster snapshot for stacked image.");
        stackedLinearImage.Dispose();

        var stackedBitmap = SkiaImageUtilities.CreateBitmapCopy(stackedImmutable);

        var integrationMilliseconds = CalculateIntegrationMilliseconds(framesIncluded);
        var latestBuffered = framesIncluded[^1];
        var originalImmutable = SkiaImageUtilities.CloneToRaster(latestBuffered.Image)
            ?? SkiaImageUtilities.SnapshotToImmutable(latestFrame.ImmutableImage, latestFrame.Image);

        return new FrameStackResult(
            latestFrame.FrameId,
            stackedBitmap,
            latestFrame.Image,
            latestFrame.Timestamp,
            latestFrame.Exposure,
            context,
            framesIncluded.Count,
            integrationMilliseconds)
        {
            StackedImmutableImage = stackedImmutable,
            OriginalImmutableImage = originalImmutable
        };
    }

    private static SKPaint CreateWeightedPaint(int frameCount)
    {
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        var weight = 1f / frameCount;

        return new SKPaint
        {
            BlendMode = SKBlendMode.Plus,
            IsAntialias = false,
            ColorF = new SKColorF(weight, weight, weight, weight)
        };
    }

    private void DrainBuffer()
    {
        int drained = 0;
        while (_buffer.TryDequeue(out var frame))
        {
            frame.Image.Dispose();
            drained++;
        }
        _bufferedIntegrationMilliseconds = 0;

        if (drained > 0 && _logger?.IsEnabled(LogLevel.Debug) == true)
        {
            _logger.LogDebug("Frame stacker buffer drained; released {Frames} frames.", drained);
        }
    }

    private static int CalculateIntegrationMilliseconds(IEnumerable<BufferedFrame> frames)
    {
        var total = 0;
        foreach (var frame in frames)
        {
            total += frame.Exposure.ExposureMilliseconds;
        }
        return total;
    }

    private void TrimBuffer(CameraConfiguration configuration)
    {
        int requiredFrames = Math.Max(configuration.StackingBufferMinimumFrames, configuration.StackingFrameCount);
        int requiredIntegration = Math.Max(0, configuration.StackingBufferIntegrationSeconds * 1_000);

        while (_buffer.Count > requiredFrames)
        {
            var candidate = _buffer.Peek();
            int newCount = _buffer.Count - 1;
            int newIntegration = _bufferedIntegrationMilliseconds - candidate.Exposure.ExposureMilliseconds;

            bool integrationSatisfied = requiredIntegration <= 0 || newIntegration >= requiredIntegration;
            if (newCount >= requiredFrames && integrationSatisfied)
            {
                var removed = _buffer.Dequeue();
                removed.Image.Dispose();
                _bufferedIntegrationMilliseconds = newIntegration;

                if (_logger?.IsEnabled(LogLevel.Trace) == true)
                {
                    _logger.LogTrace(
                        "Trimmed stacked frame buffer to {BufferCount} frames ({IntegrationMs}ms integration, minFrames {MinFrames}, minIntegration {MinIntegrationMs}ms).",
                        _buffer.Count,
                        _bufferedIntegrationMilliseconds,
                        requiredFrames,
                        requiredIntegration);
                }
            }
            else
            {
                break;
            }
        }
    }

    private static FrameMetadata CreateMetadata(FrameContext context)
        => new(
            context.Rig,
            context.LatitudeDeg,
            context.LongitudeDeg,
            context.FlipHorizontal,
            context.HorizonPadding,
            context.ApplyRefraction);

    private void EnsureBufferCompatibility(FrameMetadata? metadata)
    {
        if (_buffer.Count == 0)
        {
            return;
        }

        var hasConflict = false;
        string? bufferedRigName = null;

        foreach (var buffered in _buffer)
        {
            if (IsFrameCompatible(metadata, buffered.Metadata))
            {
                continue;
            }

            hasConflict = true;
            bufferedRigName = buffered.Metadata?.Rig.Name ?? "(none)";
            break;
        }

        if (!hasConflict)
        {
            return;
        }

        if (_logger?.IsEnabled(LogLevel.Warning) == true)
        {
            _logger.LogWarning(
                "Resetting frame stacker buffer due to mismatched frame metadata. Current rig: {CurrentRig}, Buffered rig: {BufferedRig}.",
                metadata?.Rig.Name ?? "(none)",
                bufferedRigName ?? "(none)");
        }

        DrainBuffer();
    }

    private static bool IsFrameCompatible(FrameMetadata? current, FrameMetadata? buffered)
    {
        if (current is null)
        {
            return buffered is null;
        }

        if (buffered is null)
        {
            return false;
        }

        return IsMetadataCompatible(current, buffered);
    }

    private static bool IsMetadataCompatible(FrameMetadata current, FrameMetadata buffered)
    {
        if (!Equals(current.Rig, buffered.Rig))
        {
            return false;
        }

        if (current.FlipHorizontal != buffered.FlipHorizontal || current.ApplyRefraction != buffered.ApplyRefraction)
        {
            return false;
        }

        if (!NullableDoubleEquals(current.HorizonPadding, buffered.HorizonPadding))
        {
            return false;
        }

        const double coordinateTolerance = 1e-6;
        if (Math.Abs(current.LatitudeDeg - buffered.LatitudeDeg) > coordinateTolerance)
        {
            return false;
        }

        if (Math.Abs(current.LongitudeDeg - buffered.LongitudeDeg) > coordinateTolerance)
        {
            return false;
        }

        return true;
    }

    private static bool NullableDoubleEquals(double? left, double? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return Math.Abs(left.Value - right.Value) <= 1e-6;
    }

    private IReadOnlyList<BufferedFrame> GetFramesForStack(int stackCount)
    {
        if (_buffer.Count <= stackCount)
        {
            return _buffer.ToArray();
        }

        var array = _buffer.ToArray();
        int startIndex = array.Length - stackCount;
        var result = new BufferedFrame[stackCount];
        Array.Copy(array, startIndex, result, 0, stackCount);
        return result;
    }

    public void Dispose()
    {
        DrainBuffer();
    }
}
