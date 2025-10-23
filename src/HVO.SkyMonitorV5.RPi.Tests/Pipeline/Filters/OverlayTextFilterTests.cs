using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline.Filters;

[TestClass]
public sealed class OverlayTextFilterTests
{
    [TestMethod]
    public async Task ApplyAsync_PreservesLinearGradientOutsideOverlayBounds()
    {
        using var pipelineOptions = new TestOptionsMonitor<CameraPipelineOptions>(new CameraPipelineOptions
        {
            EnableImageOverlays = true,
            OverlayTextFormat = "yyyy-MM-dd HH:mm:ss"
        });

        using var locationOptions = new TestOptionsMonitor<ObservatoryLocationOptions>(new ObservatoryLocationOptions
        {
            LatitudeDegrees = 35.1987,
            LongitudeDegrees = -114.0539,
            TimeZoneId = "America/Phoenix"
        });

        var filter = new OverlayTextFilter(pipelineOptions, locationOptions);
        var configuration = CameraConfiguration.FromOptions(pipelineOptions.CurrentValue);

        const int width = 256;
        const int height = 192;
        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var stackedBitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.75f,
            greenScale: 0.45f,
            blueValue: 0.32f);

        using var gradientImage = SkiaTestImageFactory.CreateLinearGradientImage(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.75f,
            greenScale: 0.45f,
            blueValue: 0.32f);

        using var surfacePool = new SkiaSurfacePool();
        var surfaceLease = surfacePool.RentLinearSurface(width, height);
        var surface = surfaceLease.Surface;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(gradientImage, 0, 0);
        surface.Canvas.Flush();

        var filterFrame = new FilterFrame(surfaceLease);

        var stackResult = new FrameStackResult(
            Guid.NewGuid(),
            stackedBitmap,
            stackedBitmap,
            DateTimeOffset.Parse("2025-03-01T08:30:00Z", CultureInfo.InvariantCulture),
            new ExposureSettings(1200, 240, false, false),
            Context: null,
            FramesStacked: 3,
            IntegrationMilliseconds: 3600);

        using var baselineImage = filterFrame.SnapshotImage();
        var baseline = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(baselineImage, SKColorType.Bgra8888);

        await filter.ApplyAsync(filterFrame, stackResult, configuration, renderContext: null, CancellationToken.None);

        using var processedImage = filterFrame.SnapshotImage();
        var processed = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(processedImage, SKColorType.Bgra8888);

        filterFrame.Dispose();

        var channels = 4;
        var rowStride = width * channels;
        var tolerance = 2f / 255f;

        var changeDetected = false;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowStride;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = rowOffset + x * channels;
                var pixelChanged = false;

                for (var channel = 0; channel < 3; channel++)
                {
                    var index = pixelOffset + channel;
                    var delta = Math.Abs(baseline[index] - processed[index]);
                    if (delta > tolerance)
                    {
                        changeDetected = true;
                        pixelChanged = true;
                        if (x < minX)
                        {
                            minX = x;
                        }
                        if (y < minY)
                        {
                            minY = y;
                        }
                        if (x > maxX)
                        {
                            maxX = x;
                        }
                        if (y > maxY)
                        {
                            maxY = y;
                        }
                        break;
                    }
                }

                if (!pixelChanged)
                {
                    continue;
                }
            }
        }

        Assert.IsTrue(changeDetected, "Overlay text filter should modify pixel values within the overlay region.");

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowStride;
            for (var x = 0; x < width; x++)
            {
                if (x >= minX && x <= maxX && y >= minY && y <= maxY)
                {
                    continue;
                }

                var pixelOffset = rowOffset + x * channels;
                for (var channel = 0; channel < 3; channel++)
                {
                    var index = pixelOffset + channel;
                    var delta = Math.Abs(baseline[index] - processed[index]);
                    Assert.IsLessThanOrEqualTo(tolerance, delta, $"Gradient deviation {delta:F4} at ({x}, {y}) channel {channel} exceeded tolerance {tolerance:F4}.");
                }
            }
        }
    }
}
