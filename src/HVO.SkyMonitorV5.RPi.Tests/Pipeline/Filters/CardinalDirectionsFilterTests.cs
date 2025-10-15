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
using HVO.SkyMonitorV5.RPi.Pipeline.Overlays;
using HVO.SkyMonitorV5.RPi.Skia;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline.Filters;

[TestClass]
public sealed class CardinalDirectionsFilterTests
{
    [TestMethod]
    public async Task ApplyAsync_FilterFrame_DrawsCardinalMarkersAroundProjectedCenter()
    {
        using var optionsMonitor = new TestOptionsMonitor<CardinalDirectionsOptions>(new CardinalDirectionsOptions());
        using var assetCache = new OverlayAssetCache();
        using var filter = new CardinalDirectionsFilter(optionsMonitor, NullLogger<CardinalDirectionsFilter>.Instance, assetCache);

        const int width = 320;
        const int height = 240;

        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var stackedBitmap = SkiaTestImageFactory.CreateLinearGradientBitmap(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.42f,
            greenScale: 0.58f,
            blueValue: 0.37f);
        using var gradientImage = SkiaTestImageFactory.CreateLinearGradientImage(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul,
            colorSpace,
            redScale: 0.42f,
            greenScale: 0.58f,
            blueValue: 0.37f);

        using var surfacePool = new SkiaSurfacePool();
        var surfaceLease = surfacePool.RentLinearSurface(width, height);
        var surface = surfaceLease.Surface;
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(gradientImage, 0, 0);
        surface.Canvas.Flush();

        using var filterFrame = new FilterFrame(surfaceLease);

        var (frameContext, renderContext) = CreateRenderContext();
        var options = optionsMonitor.CurrentValue;
        options.OffsetXPixels = (float)(width / 2f - renderContext.Projector.Cx);
        options.OffsetYPixels = (float)(height / 2f - renderContext.Projector.Cy);
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

        var expectedCenter = new SKPoint(
            (float)(renderContext.Projector.Cx + options.OffsetXPixels),
            (float)(renderContext.Projector.Cy + options.OffsetYPixels));
        var expectedRadius = Math.Max(8f, Math.Min(width, height) / 2f + options.RadiusOffsetPixels);

        var changeDetected = false;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        var eastDetected = false;
        var westDetected = false;
        var northDetected = false;
        var southDetected = false;

        const double axisThreshold = Math.PI / 6.0; // 30 degrees tolerance for each cardinal axis.

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowStride;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = rowOffset + x * channels;
                var pixelChanged = false;

                for (var channel = 0; channel < 3; channel++)
                {
                    var delta = Math.Abs(baseline[pixelOffset + channel] - processed[pixelOffset + channel]);
                    if (delta > tolerance)
                    {
                        changeDetected = true;
                        pixelChanged = true;
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                        break;
                    }
                }

                if (!pixelChanged)
                {
                    continue;
                }

                var dx = x - expectedCenter.X;
                var dy = y - expectedCenter.Y;
                var angle = Math.Atan2(dy, dx);

                if (Math.Abs(angle) <= axisThreshold)
                {
                    eastDetected = true;
                }
                else if (Math.Abs(Math.Abs(angle) - Math.PI) <= axisThreshold)
                {
                    westDetected = true;
                }
                else if (Math.Abs(angle + Math.PI / 2.0) <= axisThreshold)
                {
                    northDetected = true;
                }
                else if (Math.Abs(angle - Math.PI / 2.0) <= axisThreshold)
                {
                    southDetected = true;
                }
            }
        }

        Assert.IsTrue(changeDetected, "Cardinal directions overlay should modify the frame.");
        Assert.IsTrue(eastDetected, "Expected changes along the east cardinal axis.");
        Assert.IsTrue(westDetected, "Expected changes along the west cardinal axis.");
        Assert.IsTrue(northDetected, "Expected changes along the north cardinal axis.");
        Assert.IsTrue(southDetected, "Expected changes along the south cardinal axis.");

        var overlayCenterX = (minX + maxX) / 2f;
        var overlayCenterY = (minY + maxY) / 2f;
        const float centerTolerancePixels = 12f;

        Assert.IsTrue(
            Math.Abs(overlayCenterX - expectedCenter.X) <= centerTolerancePixels,
            $"Overlay center X deviated from projector center: expected {expectedCenter.X:F2}, observed {overlayCenterX:F2}.");
        Assert.IsTrue(
            Math.Abs(overlayCenterY - expectedCenter.Y) <= centerTolerancePixels,
            $"Overlay center Y deviated from projector center: expected {expectedCenter.Y:F2}, observed {overlayCenterY:F2}.");

        Assert.IsTrue(
            maxX >= expectedCenter.X + expectedRadius * 0.5f,
            "Overlay should extend toward the eastern edge consistent with the configured radius.");
        Assert.IsTrue(
            minX <= expectedCenter.X - expectedRadius * 0.5f,
            "Overlay should extend toward the western edge consistent with the configured radius.");
        Assert.IsTrue(
            maxY >= expectedCenter.Y + expectedRadius * 0.35f,
            "Overlay should extend toward the southern edge consistent with the configured radius.");
        Assert.IsTrue(
            minY <= expectedCenter.Y - expectedRadius * 0.35f,
            "Overlay should extend toward the northern edge consistent with the configured radius.");

        frameContext.Dispose();
    }

    private static FrameStackResult CreateStackResult(SKBitmap stackedBitmap, FrameContext frameContext)
    {
        var frameId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2025-03-02T06:45:00Z", CultureInfo.InvariantCulture);
        var exposure = new ExposureSettings(900, 220, false, false);

        return new FrameStackResult(
            frameId,
            stackedBitmap,
            stackedBitmap,
            timestamp,
            exposure,
            frameContext,
            FramesStacked: 4,
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
