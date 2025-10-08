#nullable enable

using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Models;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Maintains a rolling buffer of frames and combines them using a color-preserving,
/// gamma-aware averaging strategy when stacking is enabled.
/// </summary>
public sealed class RollingFrameStacker : IFrameStacker
{
    private sealed record BufferedFrame(SKBitmap Image, ExposureSettings Exposure);

    private readonly Queue<BufferedFrame> _buffer = new();
    private int _bufferedIntegrationMilliseconds;

    public FrameStackResult Accumulate(CapturedImage capture, CameraConfiguration configuration)
    {
        var frameContext = capture.Context;

        if (!configuration.EnableStacking || configuration.StackingFrameCount <= 1)
        {
            DrainBuffer();
            return new FrameStackResult(
                capture.Image,
                capture.Image,
                capture.Timestamp,
                capture.Exposure,
                frameContext,
                1,
                capture.Exposure.ExposureMilliseconds);
        }

        var bufferedBitmap = capture.Image.Copy() ?? throw new InvalidOperationException("Failed to copy captured bitmap for buffering.");
        _buffer.Enqueue(new BufferedFrame(bufferedBitmap, capture.Exposure));
        _bufferedIntegrationMilliseconds += capture.Exposure.ExposureMilliseconds;

        TrimBuffer(configuration);

        if (_buffer.Count < configuration.StackingFrameCount)
        {
            return new FrameStackResult(
                capture.Image,
                capture.Image,
                capture.Timestamp,
                capture.Exposure,
                frameContext,
                1,
                capture.Exposure.ExposureMilliseconds);
        }

        try
        {
            var framesForStack = GetFramesForStack(configuration.StackingFrameCount);
            return AverageFrames(framesForStack, capture, frameContext);
        }
        catch
        {
            return new FrameStackResult(
                capture.Image,
                capture.Image,
                capture.Timestamp,
                capture.Exposure,
                frameContext,
                1,
                capture.Exposure.ExposureMilliseconds);
        }
    }

    public void Reset() => DrainBuffer();

    private static FrameStackResult AverageFrames(IReadOnlyList<BufferedFrame> frames, CapturedImage latestFrame, FrameContext? context)
    {
        if (frames.Count == 0)
        {
            return new FrameStackResult(
                latestFrame.Image,
                latestFrame.Image,
                latestFrame.Timestamp,
                latestFrame.Exposure,
                context,
                1,
                latestFrame.Exposure.ExposureMilliseconds);
        }

        var firstBitmap = frames[0].Image;
        int width = firstBitmap.Width;
        int height = firstBitmap.Height;

        if (width == 0 || height == 0)
        {
            return new FrameStackResult(
                latestFrame.Image,
                latestFrame.Image,
                latestFrame.Timestamp,
                latestFrame.Exposure,
                context,
                1,
                latestFrame.Exposure.ExposureMilliseconds);
        }

        var accR = new double[width * height];
        var accG = new double[width * height];
        var accB = new double[width * height];
        var accA = new double[width * height];

        var framesIncluded = new List<BufferedFrame>(frames.Count);

        foreach (var frame in frames)
        {
            var bitmap = frame.Image;
            if (bitmap.Width != width || bitmap.Height != height)
            {
                continue;
            }

            AccumulateLinear(accR, accG, accB, accA, bitmap);
            framesIncluded.Add(frame);
        }

        if (framesIncluded.Count == 0)
        {
            return new FrameStackResult(
                latestFrame.Image,
                latestFrame.Image,
                latestFrame.Timestamp,
                latestFrame.Exposure,
                context,
                1,
                latestFrame.Exposure.ExposureMilliseconds);
        }

    var stackedImage = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
    WriteAverageFromLinear(accR, accG, accB, accA, stackedImage);

        var integrationMilliseconds = CalculateIntegrationMilliseconds(framesIncluded);

        return new FrameStackResult(
            stackedImage,
            latestFrame.Image,
            latestFrame.Timestamp,
            latestFrame.Exposure,
            context,
            framesIncluded.Count,
            integrationMilliseconds);
    }

    private static void AccumulateLinear(double[] accR, double[] accG, double[] accB, double[] accA, SKBitmap bitmap)
    {
        var bytes = bitmap.GetPixelSpan();
        int width = bitmap.Width;
        int height = bitmap.Height;
        int rowBytes = bitmap.RowBytes;
        int bpp = bitmap.BytesPerPixel;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * rowBytes;
            for (int x = 0; x < width; x++)
            {
                int pixelOffset = rowOffset + x * bpp;

                byte b = bytes[pixelOffset + 0];
                byte g = bytes[pixelOffset + 1];
                byte r = bytes[pixelOffset + 2];
                int index = y * width + x;

                accR[index] += SrgbToLinear(r);
                accG[index] += SrgbToLinear(g);
                accB[index] += SrgbToLinear(b);
                accA[index] += 1.0;
            }
        }
    }

    private static void WriteAverageFromLinear(double[] accR, double[] accG, double[] accB, double[] accA, SKBitmap bitmap)
    {
        var span = bitmap.GetPixelSpan();
        int pixelCount = bitmap.Width * bitmap.Height;

        for (int p = 0, i = 0; p < pixelCount; p++, i += 4)
        {
            double rl = accA[p] > 0 ? accR[p] / accA[p] : 0;
            double gl = accA[p] > 0 ? accG[p] / accA[p] : 0;
            double bl = accA[p] > 0 ? accB[p] / accA[p] : 0;

            span[i + 0] = LinearToSrgb(bl);
            span[i + 1] = LinearToSrgb(gl);
            span[i + 2] = LinearToSrgb(rl);
            span[i + 3] = 255;
        }
    }

    private void DrainBuffer()
    {
        while (_buffer.TryDequeue(out var frame))
        {
            frame.Image.Dispose();
        }
        _bufferedIntegrationMilliseconds = 0;
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
            }
            else
            {
                break;
            }
        }
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

    private static double SrgbToLinear(byte v)
    {
        double c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte LinearToSrgb(double l)
    {
        l = Math.Clamp(l, 0.0, 1.0);
        double c = l <= 0.0031308 ? 12.92 * l : 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(c * 255.0), 0, 255);
    }
}
