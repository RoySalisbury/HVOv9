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
            // If anything goes wrong, just pass through the latest frame unchanged.
            return new FrameStackResult(frame, 1, frame.Exposure.ExposureMilliseconds);
        }
    }

    public void Reset() => DrainBuffer();

    // ------------------------------------------------------------
    // Core averaging (gamma-aware, alpha-weighted)
    // ------------------------------------------------------------

    private static FrameStackResult AverageFrames(IReadOnlyList<CameraFrame> frames)
    {
        if (frames.Count == 0)
            throw new InvalidOperationException("No frames provided for stacking.");

        FramePixelBuffer? firstBuffer = null;
        foreach (var candidate in frames)
        {
            firstBuffer = candidate.RawPixelBuffer;
            if (firstBuffer is not null) break;
        }

        if (firstBuffer is null)
        {
            var fallback = frames[^1];
            return new FrameStackResult(fallback, 1, fallback.Exposure.ExposureMilliseconds);
        }

        int width = firstBuffer.Width;
        int height = firstBuffer.Height;

        // Accumulators in linear-light space
        var accR = new double[width * height];
        var accG = new double[width * height];
        var accB = new double[width * height];
        var accA = new double[width * height]; // alpha (0..1) to weight RGB

        var framesIncluded = new List<CameraFrame>(frames.Count);

        foreach (var frame in frames)
        {
            var buffer = frame.RawPixelBuffer;
            if (buffer is null) continue;
            if (buffer.Width != width || buffer.Height != height) continue;

            AccumulateLinear(accR, accG, accB, accA, buffer);
            framesIncluded.Add(frame);
        }

        if (framesIncluded.Count == 0)
        {
            var fallback = frames[^1];
            return new FrameStackResult(fallback, 1, fallback.Exposure.ExposureMilliseconds);
        }

        int n = framesIncluded.Count;

        using var averagedBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888));
        WriteAverageFromLinear(accR, accG, accB, accA, averagedBitmap, n);

        var averagedPixelBuffer = FramePixelBuffer.FromBitmap(averagedBitmap);

        using var image = SKImage.FromBitmap(averagedBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90); // keep JPEG to match prior behavior

        var lastFrame = framesIncluded[^1];
        var integrationMilliseconds = CalculateIntegrationMilliseconds(framesIncluded);

        return new FrameStackResult(
            new CameraFrame(
                lastFrame.Timestamp,
                lastFrame.Exposure,
                data.ToArray(),
                "image/jpeg",
                averagedPixelBuffer),
            n,
            integrationMilliseconds);
    }

    /// <summary>
    /// Add one frame into the linear-light accumulators (alpha-weighted).
    /// </summary>
    private static void AccumulateLinear(double[] accR, double[] accG, double[] accB, double[] accA, FramePixelBuffer buffer)
    {
        var bytes = buffer.PixelBytes;
        int width = buffer.Width;
        int height = buffer.Height;
        int rowBytes = buffer.RowBytes;
        int bpp = buffer.BytesPerPixel;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * rowBytes;
            for (int x = 0; x < width; x++)
            {
                int pixelOffset = rowOffset + x * bpp;

                byte b = bytes[pixelOffset + 0];
                byte g = bytes[pixelOffset + 1];
                byte r = bytes[pixelOffset + 2];
                byte a = bytes[pixelOffset + 3];

                // Convert sRGB -> linear (0..1). Use alpha in [0..1] as weight.
                double al = a / 255.0;
                if (al <= 0) continue;

                int p = y * width + x;

                accR[p] += SrgbToLinear(r) * al;
                accG[p] += SrgbToLinear(g) * al;
                accB[p] += SrgbToLinear(b) * al;
                accA[p] += al;
            }
        }
    }

    /// <summary>
    /// Convert linear accumulators back to sRGB and write into the output bitmap.
    /// </summary>
    private static void WriteAverageFromLinear(double[] accR, double[] accG, double[] accB, double[] accA, SKBitmap bitmap, int frameCount)
    {
        var span = bitmap.GetPixelSpan();
        int pixelCount = bitmap.Width * bitmap.Height;

        for (int p = 0, i = 0; p < pixelCount; p++, i += 4)
        {
            double al = accA[p] / frameCount;                     // average alpha 0..1
            double rl = (accA[p] > 0) ? (accR[p] / accA[p]) : 0;  // un-premultiply in linear
            double gl = (accA[p] > 0) ? (accG[p] / accA[p]) : 0;
            double bl = (accA[p] > 0) ? (accB[p] / accA[p]) : 0;

            span[i + 0] = LinearToSrgb(bl);
            span[i + 1] = LinearToSrgb(gl);
            span[i + 2] = LinearToSrgb(rl);
            span[i + 3] = (byte)Math.Clamp(Math.Round(al * 255.0), 0, 255);
        }
    }

    // ------------------------------------------------------------
    // Utilities (gamma conversion, buffer handling, windowing)
    // ------------------------------------------------------------

    private static SKBitmap LoadBitmap(byte[] bytes)
    {
        var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null)
            throw new InvalidOperationException("Unable to decode frame for stacking.");
        return bitmap;
    }

    private void DrainBuffer()
    {
        _buffer.Clear();
        _bufferedIntegrationMilliseconds = 0;
    }

    private static CameraFrame EnsureFrameHasPixelBuffer(CameraFrame frame)
    {
        if (frame.RawPixelBuffer is not null) return frame;

        using var bitmap = LoadBitmap(frame.ImageBytes);
        var buffer = FramePixelBuffer.FromBitmap(bitmap);
        return frame with { RawPixelBuffer = buffer };
    }

    private static int CalculateIntegrationMilliseconds(IEnumerable<CameraFrame> frames)
    {
        int total = 0;
        foreach (var f in frames) total += f.Exposure.ExposureMilliseconds;
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
                _buffer.Dequeue();
                _bufferedIntegrationMilliseconds = newIntegration;
            }
            else break;
        }
    }

    private IReadOnlyList<CameraFrame> GetFramesForStack(int stackCount)
    {
        if (_buffer.Count <= stackCount) return _buffer.ToArray();
        var array = _buffer.ToArray();
        int startIndex = array.Length - stackCount;
        var result = new CameraFrame[stackCount];
        Array.Copy(array, startIndex, result, 0, stackCount);
        return result;
    }

    // ---- sRGB <-> linear helpers (Rec.709 / sRGB transfer) ----
    private static double SrgbToLinear(byte v)
    {
        double c = v / 255.0;
        return (c <= 0.04045) ? (c / 12.92) : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte LinearToSrgb(double l)
    {
        l = Math.Clamp(l, 0.0, 1.0);
        double c = (l <= 0.0031308) ? (12.92 * l) : 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(c * 255.0), 0, 255);
    }
}
