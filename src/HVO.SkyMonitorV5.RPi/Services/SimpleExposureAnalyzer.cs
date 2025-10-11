#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Models;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Provides a lightweight luminance-based exposure analysis. It samples a subset of pixels to make quick decisions.
/// </summary>
public sealed class SimpleExposureAnalyzer : IExposureAnalyzer
{
    private readonly ILogger<SimpleExposureAnalyzer>? _logger;

    private const double TargetAverageLuminance = 140.0;
    private const double UpperTolerance = 30.0;
    private const double LowerTolerance = 40.0;
    private const int MinExposureMilliseconds = 5;
    private const int MaxExposureMilliseconds = 60_000;
    private const int MinGain = 0;
    private const int MaxGain = 500;

    public SimpleExposureAnalyzer(ILogger<SimpleExposureAnalyzer>? logger = null)
    {
        _logger = logger;
    }

    public ExposureAnalysisResult Analyze(CapturedImage capturedFrame, CameraConfiguration configuration)
    {
        if (capturedFrame is null)
        {
            throw new ArgumentNullException(nameof(capturedFrame));
        }

        var metrics = ComputeMetrics(capturedFrame.Image);
        var lighting = ClassifyLighting(metrics.AverageLuminance);

        var suggested = ComputeSuggestion(capturedFrame.Exposure, metrics, lighting);

        if (suggested is not null)
        {
            suggested = ClampExposure(suggested);
        }

        var notes = suggested is null
            ? "Exposure within acceptable range."
            : BuildNotes(capturedFrame.Exposure, suggested, metrics);

        return new ExposureAnalysisResult(
            capturedFrame.Exposure,
            suggested,
            lighting,
            metrics,
            notes);
    }

    private static ExposureMetrics ComputeMetrics(SkiaSharp.SKBitmap bitmap)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width <= 0 || height <= 0)
        {
            return new ExposureMetrics(0, 0, 0, 0);
        }

        var totalPixels = width * (long)height;
        var targetSamples = Math.Clamp(totalPixels / 400, 1, 10_000);
        var step = (int)Math.Max(1, totalPixels / targetSamples);

        var raster = bitmap.PeekPixels();
        var colorType = bitmap.ColorType;

        double sum = 0;
        double min = double.MaxValue;
        double max = double.MinValue;
        var samples = 0;

        if (raster is null)
        {
            return new ExposureMetrics(0, 0, 0, 0);
        }

        var info = raster.Info;
        var pixelSpan = raster.GetPixelSpan();
        if (pixelSpan.Length == 0)
        {
            return new ExposureMetrics(0, 0, 0, 0);
        }

        var pixelStride = info.BytesPerPixel;
        var totalBytes = pixelSpan.Length;

        for (var index = 0; index <= totalBytes - pixelStride; index += step * pixelStride)
        {
            var luminance = ExtractLuminance(pixelSpan, index, colorType);
            sum += luminance;
            if (luminance < min)
            {
                min = luminance;
            }

            if (luminance > max)
            {
                max = luminance;
            }

            samples++;
        }

        if (samples == 0)
        {
            return new ExposureMetrics(0, 0, 0, 0);
        }

        var average = sum / samples;
        return new ExposureMetrics(average, min, max, samples);
    }

    private static double ExtractLuminance(ReadOnlySpan<byte> buffer, int index, SKColorType colorType)
    {
        // Assume standard 8-bit per channel format.
        byte r;
        byte g;
        byte b;

        switch (colorType)
        {
            case SkiaSharp.SKColorType.Gray8:
                var gray = buffer[index];
                r = g = b = gray;
                break;
            default:
                b = buffer[index + 0];
                g = buffer[index + 1];
                r = buffer[index + 2];
                break;
        }

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static ExposureLightingCondition ClassifyLighting(double averageLuminance)
    {
        if (averageLuminance <= 0)
        {
            return ExposureLightingCondition.Unknown;
        }

        if (averageLuminance >= 170)
        {
            return ExposureLightingCondition.Daylight;
        }

        if (averageLuminance >= 90)
        {
            return ExposureLightingCondition.Twilight;
        }

        return ExposureLightingCondition.Night;
    }

    private ExposureSettings? ComputeSuggestion(ExposureSettings current, ExposureMetrics metrics, ExposureLightingCondition lighting)
    {
        if (metrics.SampleCount == 0)
        {
            return null;
        }

        var average = metrics.AverageLuminance;

        if (average > TargetAverageLuminance + UpperTolerance)
        {
            var scale = 0.85;
            var nextExposure = Math.Max((int)Math.Round(current.ExposureMilliseconds * scale), MinExposureMilliseconds);
            var nextGain = Math.Max((int)Math.Round(current.Gain * scale), MinGain);
            return new ExposureSettings(nextExposure, nextGain, current.AutoExposure, current.AutoGain);
        }

        if (average < TargetAverageLuminance - LowerTolerance)
        {
            var scale = lighting == ExposureLightingCondition.Daylight ? 1.10 : 1.20;
            var nextExposure = Math.Min((int)Math.Round(current.ExposureMilliseconds * scale), MaxExposureMilliseconds);
            var gainBump = lighting == ExposureLightingCondition.Daylight ? 5 : 10;
            var nextGain = Math.Min(current.Gain + gainBump, MaxGain);
            return new ExposureSettings(nextExposure, nextGain, current.AutoExposure, current.AutoGain);
        }

        return null;
    }

    private static ExposureSettings ClampExposure(ExposureSettings exposure)
    {
        var clampedExposure = Math.Clamp(exposure.ExposureMilliseconds, MinExposureMilliseconds, MaxExposureMilliseconds);
        var clampedGain = Math.Clamp(exposure.Gain, MinGain, MaxGain);
        if (clampedExposure == exposure.ExposureMilliseconds && clampedGain == exposure.Gain)
        {
            return exposure;
        }

        return exposure with
        {
            ExposureMilliseconds = clampedExposure,
            Gain = clampedGain
        };
    }

    private string BuildNotes(ExposureSettings current, ExposureSettings suggested, ExposureMetrics metrics)
    {
        var adjective = suggested.ExposureMilliseconds > current.ExposureMilliseconds ? "Increasing" : "Decreasing";
        return $"{adjective} exposure to {suggested.ExposureMilliseconds} ms / gain {suggested.Gain} (avg luminance {metrics.AverageLuminance:F1}).";
    }
}
