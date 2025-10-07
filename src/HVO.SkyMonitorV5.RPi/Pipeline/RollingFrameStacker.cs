#nullable enable

using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Models;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Maintains a rolling buffer of frames and combines them using a simple averaging strategy when stacking is enabled.
/// </summary>
public sealed class RollingFrameStacker : IFrameStacker
{
    private readonly Queue<CameraFrame> _buffer = new();
    private int _bufferedIntegrationMilliseconds;

    public FrameStackResult Accumulate(CameraFrame frame, CameraConfiguration configuration)
    {
        frame = EnsureFrameHasPixelBuffer(frame);

        if (!configuration.EnableStacking || configuration.StackingFrameCount <= 1)
        {
            DrainBuffer();
            return new FrameStackResult(frame, 1, frame.Exposure.ExposureMilliseconds);
        }

        _buffer.Enqueue(frame);
        _bufferedIntegrationMilliseconds += frame.Exposure.ExposureMilliseconds;

        TrimBuffer(configuration);

        if (_buffer.Count < configuration.StackingFrameCount)
        {
            return new FrameStackResult(frame, 1, frame.Exposure.ExposureMilliseconds);
        }

        try
        {
            var framesForStack = GetFramesForStack(configuration.StackingFrameCount);
            return AverageFrames(framesForStack);
        }
        catch
        {
            return new FrameStackResult(frame, 1, frame.Exposure.ExposureMilliseconds);
        }
    }

    public void Reset()
    {
        DrainBuffer();
    }

    private static FrameStackResult AverageFrames(IReadOnlyList<CameraFrame> frames)
    {
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("No frames provided for stacking.");
        }

        FramePixelBuffer? firstBuffer = null;
        foreach (var candidate in frames)
        {
            firstBuffer = candidate.RawPixelBuffer;
            if (firstBuffer is not null)
            {
                break;
            }
        }

        if (firstBuffer is null)
        {
            var fallback = frames[^1];
            return new FrameStackResult(fallback, 1, fallback.Exposure.ExposureMilliseconds);
        }

        var width = firstBuffer.Width;
        var height = firstBuffer.Height;
        var accumulator = new double[width * height * 3];

        var framesIncluded = new List<CameraFrame>();

        foreach (var frame in frames)
        {
            var buffer = frame.RawPixelBuffer;
            if (buffer is null)
            {
                continue;
            }

            if (buffer.Width != width || buffer.Height != height)
            {
                continue;
            }

            Accumulate(accumulator, buffer);
            framesIncluded.Add(frame);
        }

        if (framesIncluded.Count == 0)
        {
            var fallback = frames[^1];
            return new FrameStackResult(fallback, 1, fallback.Exposure.ExposureMilliseconds);
        }

        var frameCount = framesIncluded.Count;

        using var averagedBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888));
        WriteAverageIntoBitmap(accumulator, averagedBitmap, frameCount);

        var averagedPixelBuffer = FramePixelBuffer.FromBitmap(averagedBitmap);

        using var image = SKImage.FromBitmap(averagedBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        var lastFrame = framesIncluded[^1];
        var integrationMilliseconds = CalculateIntegrationMilliseconds(framesIncluded);

        return new FrameStackResult(
            new CameraFrame(
                lastFrame.Timestamp,
                lastFrame.Exposure,
                data.ToArray(),
                "image/jpeg",
                averagedPixelBuffer),
            frameCount,
            integrationMilliseconds);
    }

    private static void Accumulate(double[] accumulator, FramePixelBuffer buffer)
    {
        var bytes = buffer.PixelBytes;
        var width = buffer.Width;
        var height = buffer.Height;
        var rowBytes = buffer.RowBytes;
        var bytesPerPixel = buffer.BytesPerPixel;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = rowOffset + x * bytesPerPixel;
                var b = bytes[pixelOffset];
                var g = bytes[pixelOffset + 1];
                var r = bytes[pixelOffset + 2];

                var accumulatorIndex = (y * width + x) * 3;
                accumulator[accumulatorIndex] += r;
                accumulator[accumulatorIndex + 1] += g;
                accumulator[accumulatorIndex + 2] += b;
            }
        }
    }

    private static SKBitmap LoadBitmap(byte[] bytes)
    {
        var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null)
        {
            throw new InvalidOperationException("Unable to decode frame for stacking.");
        }

        return bitmap;
    }

    private void DrainBuffer()
    {
        _buffer.Clear();
        _bufferedIntegrationMilliseconds = 0;
    }

    private static CameraFrame EnsureFrameHasPixelBuffer(CameraFrame frame)
    {
        if (frame.RawPixelBuffer is not null)
        {
            return frame;
        }

        using var bitmap = LoadBitmap(frame.ImageBytes);
        var buffer = FramePixelBuffer.FromBitmap(bitmap);
        return frame with { RawPixelBuffer = buffer };
    }

    private static void WriteAverageIntoBitmap(double[] accumulator, SKBitmap bitmap, int frameCount)
    {
        var pixelCount = bitmap.Width * bitmap.Height;
        var outputSpan = bitmap.GetPixelSpan();

        for (var i = 0; i < pixelCount; i++)
        {
            var accumulatorIndex = i * 3;
            var r = (byte)Math.Clamp(accumulator[accumulatorIndex] / frameCount, 0, 255);
            var g = (byte)Math.Clamp(accumulator[accumulatorIndex + 1] / frameCount, 0, 255);
            var b = (byte)Math.Clamp(accumulator[accumulatorIndex + 2] / frameCount, 0, 255);

            var pixelIndex = i * 4;
            outputSpan[pixelIndex] = b;
            outputSpan[pixelIndex + 1] = g;
            outputSpan[pixelIndex + 2] = r;
            outputSpan[pixelIndex + 3] = 255;
        }
    }

    private static int CalculateIntegrationMilliseconds(IEnumerable<CameraFrame> frames)
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
        var requiredFrames = Math.Max(configuration.StackingBufferMinimumFrames, configuration.StackingFrameCount);
        var requiredIntegration = Math.Max(0, configuration.StackingBufferIntegrationSeconds * 1_000);

        while (_buffer.Count > requiredFrames)
        {
            var candidate = _buffer.Peek();
            var newCount = _buffer.Count - 1;
            var newIntegration = _bufferedIntegrationMilliseconds - candidate.Exposure.ExposureMilliseconds;

            var integrationSatisfied = requiredIntegration <= 0 || newIntegration >= requiredIntegration;
            if (newCount >= requiredFrames && integrationSatisfied)
            {
                _buffer.Dequeue();
                _bufferedIntegrationMilliseconds = newIntegration;
            }
            else
            {
                break;
            }
        }
    }

    private IReadOnlyList<CameraFrame> GetFramesForStack(int stackCount)
    {
        if (_buffer.Count <= stackCount)
        {
            return _buffer.ToArray();
        }

        var bufferArray = _buffer.ToArray();
        var startIndex = bufferArray.Length - stackCount;
        var result = new CameraFrame[stackCount];
        Array.Copy(bufferArray, startIndex, result, 0, stackCount);
        return result;
    }
}
