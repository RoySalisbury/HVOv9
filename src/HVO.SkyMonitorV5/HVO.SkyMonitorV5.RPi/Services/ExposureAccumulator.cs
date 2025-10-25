#nullable enable

using System;
using SkiaSharp;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Provides span-centric accumulation helpers for exposure analysis metrics.
/// </summary>
internal static class ExposureAccumulator
{
    public static ExposureMetrics ComputeMetrics(SKBitmap bitmap)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width <= 0 || height <= 0)
        {
            return defaultMetrics;
        }

        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null)
        {
            return defaultMetrics;
        }

        var pixelSpan = pixmap.GetPixelSpan();
        if (pixelSpan.Length == 0)
        {
            return defaultMetrics;
        }

        var info = pixmap.Info;
        var pixelStride = info.BytesPerPixel;
        if (pixelStride <= 0)
        {
            return defaultMetrics;
        }

        var totalPixels = width * (long)height;
        var targetSamples = Math.Clamp(totalPixels / 400, 1, 10_000);
        var step = (int)Math.Max(1, totalPixels / targetSamples);
        var increment = Math.Max(pixelStride, step * pixelStride);

        var state = new AccumulatorState();
        if (!TryAccumulateVectorized(pixelSpan, info.ColorType, pixelStride, increment, ref state))
        {
            AccumulateScalar(pixelSpan, info.ColorType, pixelStride, increment, ref state);
        }

        return state.ToMetrics();
    }

    private static void AccumulateScalar(ReadOnlySpan<byte> buffer, SKColorType colorType, int pixelStride, int increment, ref AccumulatorState state)
    {
        for (var index = 0; index <= buffer.Length - pixelStride; index += increment)
        {
            var luminance = ExtractLuminance(buffer, index, colorType);
            state.AddSample(luminance);
        }
    }

    private static double ExtractLuminance(ReadOnlySpan<byte> buffer, int index, SKColorType colorType)
    {
        switch (colorType)
        {
            case SKColorType.Gray8:
                return buffer[index];
            default:
                var b = buffer[index + 0];
                var g = buffer[index + 1];
                var r = buffer[index + 2];
                return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        }
    }

    private static bool TryAccumulateVectorized(ReadOnlySpan<byte> buffer, SKColorType colorType, int pixelStride, int increment, ref AccumulatorState state)
    {
        // TODO(dotnet10): Replace with SIMD-accelerated path when generic math and wider intrinsics ship.
        return false;
    }

    private static readonly ExposureMetrics defaultMetrics = new(0, 0, 0, 0);

    private struct AccumulatorState
    {
        private double _sum;
        private double _min;
        private double _max;
        private int _samples;

        public void AddSample(double luminance)
        {
            _sum += luminance;
            if (luminance < _min || _samples == 0)
            {
                _min = luminance;
            }

            if (luminance > _max || _samples == 0)
            {
                _max = luminance;
            }

            _samples++;
        }

        public ExposureMetrics ToMetrics()
        {
            if (_samples == 0)
            {
                return defaultMetrics;
            }

            var average = _sum / _samples;
            return new ExposureMetrics(average, _min, _max, _samples);
        }
    }
}
