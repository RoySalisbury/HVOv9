using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Benchmarks.Benchmarks;

internal static class BenchmarkDataFactory
{
    private const double Latitude = 35.1987;
    private const double Longitude = -114.0539;

    public static CapturedImage CreateCapturedImage(int width = 1920, int height = 1080)
    {
        var bitmap = new SKBitmap(width, height);
        DrawSyntheticGradient(bitmap);

        var timestamp = DateTimeOffset.UtcNow;
        var rig = RigPresets.MockAsi174_Fujinon;
        var engine = new StarFieldEngine(
            rig,
            Latitude,
            Longitude,
            timestamp.UtcDateTime,
            flipHorizontal: false,
            applyRefraction: true,
            horizonPadding: 0.95);

        var context = new FrameContext(
            rig,
            engine,
            timestamp,
            Latitude,
            Longitude,
            FlipHorizontal: false,
            HorizonPadding: 0.95,
            ApplyRefraction: true);

        var exposure = new ExposureSettings(
            ExposureMilliseconds: 1_000,
            Gain: 200,
            AutoExposure: false,
            AutoGain: false);

        return new CapturedImage(bitmap, timestamp, exposure, context);
    }

    public static FrameStackResult CreateStackResult(int width = 1920, int height = 1080)
    {
        var capture = CreateCapturedImage(width, height);
        var stacked = capture.Image.Copy();
        if (stacked is null)
        {
            throw new InvalidOperationException("Failed to clone bitmap for stacked frame.");
        }

        return new FrameStackResult(
            stacked,
            capture.Image,
            capture.Timestamp,
            capture.Exposure,
            capture.Context,
            FramesStacked: 1,
            IntegrationMilliseconds: capture.Exposure.ExposureMilliseconds);
    }

    public static void DisposeCapturedImage(CapturedImage capture)
    {
        capture.Context?.Dispose();
        capture.Image.Dispose();
    }

    public static void DisposeFrameResult(FrameStackResult result)
    {
        result.Context?.Dispose();
        if (!ReferenceEquals(result.StackedImage, result.OriginalImage))
        {
            result.StackedImage.Dispose();
        }

        result.OriginalImage.Dispose();
    }

    private static void DrawSyntheticGradient(SKBitmap bitmap)
    {
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            IsAntialias = true
        };

        var colors = new[]
        {
            SKColor.FromHsv(210, 80, 30),
            SKColor.FromHsv(240, 40, 80)
        };

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(bitmap.Width, bitmap.Height),
            colors,
            null,
            SKShaderTileMode.Clamp);

        paint.Shader = shader;
        canvas.DrawRect(new SKRect(0, 0, bitmap.Width, bitmap.Height), paint);
    }
}
