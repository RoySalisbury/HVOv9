using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline;

[TestClass]
public sealed class RollingFrameStackerTests
{
    [TestMethod]
    public void Accumulate_WhenStackingDisabled_ReturnsFrameContextWithoutDisposing()
    {
        using var captureBitmap = new SKBitmap(width: 8, height: 8);
    var (context, wasDisposed) = CreateFrameContext();
        var exposure = new ExposureSettings(500, 200, false, false);
    var capture = new CapturedImage(context.FrameId, captureBitmap, DateTimeOffset.UtcNow, exposure, context);

        var stacker = new RollingFrameStacker();
        var configuration = new CameraConfiguration(
            EnableStacking: false,
            StackingFrameCount: 1,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 1,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        var result = stacker.Accumulate(capture, configuration);

        Assert.AreSame(context, result.Context, "Frame context should be forwarded when stacking is disabled.");
        Assert.IsFalse(wasDisposed(), "Stacker should not dispose the frame context.");

        DisposeFrameResult(result);
        context.Dispose();
    }

    [TestMethod]
    public void Accumulate_WithStackingEnabled_UsesSharedContextAcrossStackedFrames()
    {
        var stacker = new RollingFrameStacker();
        var configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 2,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 2,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

    var (context, wasDisposed) = CreateFrameContext();
        var exposure = new ExposureSettings(500, 200, false, false);

        using var firstBitmap = new SKBitmap(width: 8, height: 8);
    var firstCapture = new CapturedImage(context.FrameId, firstBitmap, DateTimeOffset.UtcNow, exposure, context);
        var firstResult = stacker.Accumulate(firstCapture, configuration);
        DisposeFrameResult(firstResult);

        using var secondBitmap = new SKBitmap(width: 8, height: 8);
    var secondCapture = new CapturedImage(context.FrameId, secondBitmap, DateTimeOffset.UtcNow.AddMilliseconds(200), exposure, context);
        var stackedResult = stacker.Accumulate(secondCapture, configuration);

        Assert.AreSame(context, stackedResult.Context, "Stacked frame should retain the original frame context instance.");
        Assert.AreEqual(2, stackedResult.FramesStacked, "Stacked frame count should reflect the number of frames combined.");
        Assert.IsFalse(wasDisposed(), "Frame context should remain undisposed until the pipeline is finished.");

        DisposeFrameResult(stackedResult);
        context.Dispose();
    }

    [TestMethod]
    public void Accumulate_PartialStacksIncreaseUntilTarget()
    {
        var stacker = new RollingFrameStacker();
        var configuration = new CameraConfiguration(
            EnableStacking: true,
            StackingFrameCount: 4,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 4,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

        var exposure = new ExposureSettings(1_000, 200, false, false);
        var expectedCounts = new[] { 1, 2, 3, 4 };
        var expectedIntegration = new[] { 1_000, 2_000, 3_000, 4_000 };
        var observedCounts = new List<int>(expectedCounts.Length);

        for (var i = 0; i < expectedCounts.Length; i++)
        {
            var bitmap = new SKBitmap(width: 8, height: 8);
            var capture = new CapturedImage(Guid.NewGuid(), bitmap, DateTimeOffset.UtcNow.AddMilliseconds(i * 10), exposure, null);
            var result = stacker.Accumulate(capture, configuration);

            observedCounts.Add(result.FramesStacked);
            Assert.AreEqual(expectedIntegration[i], result.IntegrationMilliseconds, "Integration should scale with the number of stacked frames.");

            DisposeFrameResult(result);
        }

        CollectionAssert.AreEqual(expectedCounts, observedCounts, "Stacked frame count should increase as the buffer fills.");
        stacker.Dispose();
    }

    private static (FrameContext Context, Func<bool> WasDisposed) CreateFrameContext()
    {
        var rig = RigPresets.MockAsi174_Fujinon;
        var timestamp = DateTimeOffset.UtcNow;
        var engine = new StarFieldEngine(rig, TestLatitude, TestLongitude, timestamp.UtcDateTime, flipHorizontal: false, applyRefraction: true, horizonPadding: 0.95);
        var disposed = false;
        var context = new FrameContext(
            Guid.NewGuid(),
            rig,
            engine,
            timestamp,
            TestLatitude,
            TestLongitude,
            FlipHorizontal: false,
            HorizonPadding: 0.95,
            ApplyRefraction: true,
            DisposeAction: _ => disposed = true);
        return (context, () => disposed);
    }

    private static void DisposeFrameResult(FrameStackResult result)
    {
        result.StackedImage.Dispose();
            result.StackedImmutableImage?.Dispose();
        if (!ReferenceEquals(result.StackedImage, result.OriginalImage))
        {
            result.OriginalImage.Dispose();
        }
            result.OriginalImmutableImage?.Dispose();
    }

    private const double TestLatitude = 35.1987;
    private const double TestLongitude = -114.0539;
}
