using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Filters;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline.Filters;

[TestClass]
public sealed class DiagnosticsOverlayFilterTests
{
    [TestMethod]
    public async Task ApplyAsync_FilterFrame_RendersOverlayInConfiguredCorner()
    {
        using var optionsMonitor = new TestOptionsMonitor<DiagnosticsOverlayOptions>(new DiagnosticsOverlayOptions
        {
            Enabled = true,
            Corner = OverlayCorner.TopRight,
            ShowRigDetails = true,
            ShowProjectorDetails = true,
            ShowStackingMetrics = true,
            ShowContextFlags = true
        });

        using var filter = new DiagnosticsOverlayFilter(optionsMonitor, NullLogger<DiagnosticsOverlayFilter>.Instance);
        var options = optionsMonitor.CurrentValue;

        const int width = 320;
        const int height = 240;

        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var stackedBitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.55f,
            greenScale: 0.32f,
            blueValue: 0.41f);
        using var gradientImage = SkiaTestImageFactory.CreateLinearGradientImage(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.55f,
            greenScale: 0.32f,
            blueValue: 0.41f);

        using var surfacePool = new SkiaSurfacePool();
        var surfaceLease = surfacePool.RentLinearSurface(width, height);
        var surface = surfaceLease.Surface;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(gradientImage, 0, 0);
        surface.Canvas.Flush();

        using var filterFrame = new FilterFrame(surfaceLease);

        var (frameContext, renderContext) = CreateRenderContext();
        var stackResult = CreateStackResult(stackedBitmap, frameContext);
        var configuration = CreateConfiguration();

        using var baselineImage = filterFrame.SnapshotImage();
        var baseline = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(baselineImage, SKColorType.Bgra8888);

        await filter.ApplyAsync(filterFrame, stackResult, configuration, renderContext, CancellationToken.None);

        using var processedImage = filterFrame.SnapshotImage();
        var processed = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(processedImage, SKColorType.Bgra8888);

        var channels = 4;
        var rowStride = width * channels;
        var tolerance = 2f / 255f;

        var changeDetected = false;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        var partiallyBlendedEdgePixels = 0;

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
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                        if (delta < 0.6f)
                        {
                            partiallyBlendedEdgePixels++;
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

        Assert.IsTrue(changeDetected, "Diagnostics overlay should modify pixels within the overlay bounds.");
        var centerX = (minX + maxX) / 2.0f;
        var overlayHeight = maxY - minY;
        Assert.IsTrue(centerX > width / 2f, "Diagnostics overlay should be anchored on the right half for TopRight corner.");
        Assert.IsTrue(maxX > width * 0.75f, "Diagnostics overlay should reach near the right edge for TopRight corner.");
        Assert.IsTrue(minY <= options.Margin + 6f, $"Diagnostics overlay should start near the configured margin; observed top {minY} with margin {options.Margin}.");
        Assert.IsTrue(overlayHeight < height - options.Margin, $"Diagnostics overlay height {overlayHeight} should leave space below the block for TopRight corner.");
        Assert.IsTrue(partiallyBlendedEdgePixels > 0, "Expected partially blended edge pixels validating antialiasing on linear surfaces.");

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
                    Assert.IsTrue(delta <= tolerance, $"Gradient deviation {delta:F4} at ({x}, {y}) channel {channel} exceeded tolerance {tolerance:F4}.");
                }
            }
        }

        frameContext.Dispose();
    }

    [TestMethod]
    public async Task ApplyAsync_FilterFrame_SkipsWhenRenderContextMissing()
    {
        using var optionsMonitor = new TestOptionsMonitor<DiagnosticsOverlayOptions>(new DiagnosticsOverlayOptions { Enabled = true });
        using var filter = new DiagnosticsOverlayFilter(optionsMonitor, NullLogger<DiagnosticsOverlayFilter>.Instance);

        const int width = 240;
        const int height = 180;

        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var stackedBitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.35f,
            greenScale: 0.65f,
            blueValue: 0.22f);
        using var gradientImage = SkiaTestImageFactory.CreateLinearGradientImage(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.35f,
            greenScale: 0.65f,
            blueValue: 0.22f);

        using var surfacePool = new SkiaSurfacePool();
        var surfaceLease = surfacePool.RentLinearSurface(width, height);
        var surface = surfaceLease.Surface;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(gradientImage, 0, 0);
        surface.Canvas.Flush();

        using var filterFrame = new FilterFrame(surfaceLease);

        var stackResult = CreateStackResult(stackedBitmap, frameContext: null);
        var configuration = CreateConfiguration();

        using var baselineImage = filterFrame.SnapshotImage();
        var baseline = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(baselineImage, SKColorType.Bgra8888);

        await filter.ApplyAsync(filterFrame, stackResult, configuration, renderContext: null, CancellationToken.None);

        using var processedImage = filterFrame.SnapshotImage();
        var processed = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(processedImage, SKColorType.Bgra8888);

        for (var i = 0; i < baseline.Length; i++)
        {
            var delta = Math.Abs(baseline[i] - processed[i]);
            Assert.IsTrue(delta <= 1e-6f, $"Expected no change when render context is missing, observed delta {delta} at index {i}.");
        }
    }

    private static FrameStackResult CreateStackResult(SKBitmap stackedBitmap, FrameContext? frameContext)
    {
        var frameId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2025-03-01T08:30:00Z", CultureInfo.InvariantCulture);
        var exposure = new ExposureSettings(1200, 240, false, false);

        return new FrameStackResult(
            frameId,
            stackedBitmap,
            stackedBitmap,
            timestamp,
            exposure,
            frameContext,
            FramesStacked: 3,
            IntegrationMilliseconds: 3600);
    }

    private static (FrameContext Context, FrameRenderContext RenderContext) CreateRenderContext()
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var timestamp = DateTimeOffset.UtcNow;
        var engine = new StarFieldEngine(rig, 35.1987, -114.0539, timestamp.UtcDateTime, flipHorizontal: false, applyRefraction: true, horizonPadding: 0.95);
        var context = new FrameContext(
            Guid.NewGuid(),
            rig,
            engine,
            timestamp,
            35.1987,
            -114.0539,
            FlipHorizontal: false,
            HorizonPadding: 0.95,
            ApplyRefraction: true,
            DisposeAction: _ => { });

        return (context, new FrameRenderContext(context));
    }

    private static CameraConfiguration CreateConfiguration()
        => new(
            EnableStacking: true,
            StackingFrameCount: 3,
            EnableImageOverlays: true,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 3,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());
}
