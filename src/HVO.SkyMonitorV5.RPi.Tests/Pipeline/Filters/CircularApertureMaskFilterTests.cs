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
public sealed class CircularApertureMaskFilterTests
{
    [TestMethod]
    public async Task ApplyAsync_FilterFrame_DrawsMaskOutsideConfiguredAperture()
    {
        var latitude = 35.1987;
        var longitude = -114.0539;
        var timestamp = DateTimeOffset.Parse("2025-03-02T06:45:00Z", CultureInfo.InvariantCulture);

        using var optionsMonitor = new TestOptionsMonitor<CircularApertureMaskOptions>(new CircularApertureMaskOptions
        {
            OffsetXPixels = 0,
            OffsetYPixels = 0,
            RadiusOffsetPixels = 0,
            MaskColor = "#FF3300",
            MaskOpacity = 220
        });

        var filter = new CircularApertureMaskFilter(optionsMonitor);

        var (frameContext, renderContext) = CreateRenderContext(latitude, longitude, timestamp);
        var projector = renderContext.Projector;
        var width = projector.WidthPx;
        var height = projector.HeightPx;

        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var stackedBitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.42f,
            greenScale: 0.36f,
            blueValue: 0.54f);
        using var gradientImage = SkiaTestImageFactory.CreateLinearGradientImage(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.42f,
            greenScale: 0.36f,
            blueValue: 0.54f);

        using var surfacePool = new SkiaSurfacePool();
        var surfaceLease = surfacePool.RentLinearSurface(width, height);
        var surface = surfaceLease.Surface;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(gradientImage, 0, 0);
        surface.Canvas.Flush();

        using var filterFrame = new FilterFrame(surfaceLease);

        var stackResult = CreateStackResult(stackedBitmap, frameContext);
        var configuration = CreateConfiguration();

        Assert.IsTrue(filter.ShouldApply(configuration), "Filter should apply when circular mask is enabled.");

        using var baselineImage = filterFrame.SnapshotImage();
        var baseline = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(baselineImage, SKColorType.Bgra8888);

        await filter.ApplyAsync(filterFrame, stackResult, configuration, renderContext, CancellationToken.None);

        using var processedImage = filterFrame.SnapshotImage();
        var processed = SkiaTestImageFactory.GetNormalizedFloatPixelBuffer(processedImage, SKColorType.Bgra8888);

        var channels = 4;
        var rowStride = width * channels;
        var tolerance = 2f / 255f;

        static float SampleDelta(float[] before, float[] after, int stride, int x, int y, int channels)
        {
            var offset = y * stride + x * channels;
            var deltaR = Math.Abs(before[offset] - after[offset]);
            var deltaG = Math.Abs(before[offset + 1] - after[offset + 1]);
            var deltaB = Math.Abs(before[offset + 2] - after[offset + 2]);
            return Math.Max(deltaR, Math.Max(deltaG, deltaB));
        }

        var centerX = (int)MathF.Round((float)projector.Cx);
        var centerY = (int)MathF.Round((float)projector.Cy);
        centerX = Math.Clamp(centerX, 0, width - 1);
        centerY = Math.Clamp(centerY, 0, height - 1);

        var centerDelta = SampleDelta(baseline, processed, rowStride, centerX, centerY, channels);
        var cornerDelta = SampleDelta(baseline, processed, rowStride, 5, 5, channels);

        Assert.IsTrue(cornerDelta > tolerance, $"Mask should darken pixels outside the aperture (delta observed {cornerDelta:F6}).");
        Assert.IsTrue(centerDelta < tolerance, $"Mask should keep the core aperture unchanged (delta observed {centerDelta:F6}).");

        frameContext.Dispose();
    }

    private static (FrameContext Context, FrameRenderContext RenderContext) CreateRenderContext(double latitude, double longitude, DateTimeOffset timestamp)
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var engine = new StarFieldEngine(rig, latitude, longitude, timestamp.UtcDateTime, flipHorizontal: false, applyRefraction: true, horizonPadding: 0.95);
        var context = new FrameContext(
            Guid.NewGuid(),
            rig,
            engine,
            timestamp,
            latitude,
            longitude,
            FlipHorizontal: false,
            HorizonPadding: 0.95,
            ApplyRefraction: true,
            DisposeAction: _ => { });

        return (context, new FrameRenderContext(context));
    }

    private static FrameStackResult CreateStackResult(SKBitmap stackedBitmap, FrameContext frameContext)
    {
        var frameId = Guid.NewGuid();
        var exposure = new ExposureSettings(600, 180, false, false);

        return new FrameStackResult(
            frameId,
            stackedBitmap,
            stackedBitmap,
            DateTimeOffset.Parse("2025-03-02T06:45:00Z", CultureInfo.InvariantCulture),
            exposure,
            frameContext,
            FramesStacked: 4,
            IntegrationMilliseconds: 2400);
    }

    private static CameraConfiguration CreateConfiguration()
        => new(
            EnableStacking: true,
            StackingFrameCount: 3,
            EnableImageOverlays: true,
            EnableCircularApertureMask: true,
            StackingBufferMinimumFrames: 3,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());
}
