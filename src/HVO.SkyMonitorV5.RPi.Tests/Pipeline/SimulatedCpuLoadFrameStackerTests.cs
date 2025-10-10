using System;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline;

[TestClass]
public sealed class SimulatedCpuLoadFrameStackerTests
{
    [TestMethod]
    public void Accumulate_WhenProfileDisabled_ForwardsWithoutWork()
    {
        var captureBitmap = new SKBitmap(width: 4, height: 4);
        var stackedBitmap = new SKBitmap(width: 4, height: 4);

        var exposure = new ExposureSettings(100, 50, false, false);
        var capture = new CapturedImage(captureBitmap, DateTimeOffset.UtcNow, exposure, null);
        var configuration = CreateConfiguration();

        var expectedResult = new FrameStackResult(stackedBitmap, captureBitmap, capture.Timestamp, exposure, null, 1, 100);

        var inner = new Mock<IFrameStacker>(MockBehavior.Strict);
        inner.Setup(stack => stack.Accumulate(capture, configuration)).Returns(expectedResult);
        inner.Setup(stack => stack.Reset());

        var profile = SimulatedCpuLoadProfile.Disabled;
        var stacker = new SimulatedCpuLoadFrameStacker(inner.Object, profile, NullLogger<SimulatedCpuLoadFrameStacker>.Instance);

        var result = stacker.Accumulate(capture, configuration);

        Assert.AreSame(expectedResult, result, "Decorator should return the inner stacker's result when disabled.");

        stacker.Reset();
        inner.Verify(stack => stack.Reset(), Times.Once);

        DisposeFrameResult(result);
    }

    [TestMethod]
    public void Accumulate_WhenProfileEnabled_StillReturnsInnerResult()
    {
        var captureBitmap = new SKBitmap(width: 4, height: 4);
        var stackedBitmap = new SKBitmap(width: 4, height: 4);

        var exposure = new ExposureSettings(150, 60, false, false);
        var capture = new CapturedImage(captureBitmap, DateTimeOffset.UtcNow, exposure, null);
        var configuration = CreateConfiguration();

        var expectedResult = new FrameStackResult(stackedBitmap, captureBitmap, capture.Timestamp, exposure, null, 1, 120);

        var inner = new Mock<IFrameStacker>(MockBehavior.Strict);
        inner.Setup(stack => stack.Accumulate(capture, configuration)).Returns(expectedResult);

        var profile = new SimulatedCpuLoadProfile(
            Enabled: true,
            BaselineMilliseconds: 1,
            VariabilityMilliseconds: 0,
            SpikeProbability: 0,
            SpikeMultiplier: 1,
            WorkerCount: 1,
            MaximumMilliseconds: 2,
            RandomSeed: 1234);

        var stacker = new SimulatedCpuLoadFrameStacker(inner.Object, profile, NullLogger<SimulatedCpuLoadFrameStacker>.Instance);

        var result = stacker.Accumulate(capture, configuration);

        Assert.AreSame(expectedResult, result, "Decorator must return the inner stacker's FrameStackResult instance.");

        DisposeFrameResult(result);
    }

    [TestMethod]
    public void OnConfigurationChanged_ForwardsToListener()
    {
        var previous = CreateConfiguration();
        var current = previous with { EnableStacking = !previous.EnableStacking };

        var inner = new Mock<IFrameStacker>(MockBehavior.Strict);
        inner.As<IFrameStackerConfigurationListener>().Setup(l => l.OnConfigurationChanged(previous, current));

        var profile = SimulatedCpuLoadProfile.Disabled;

        var stacker = new SimulatedCpuLoadFrameStacker(inner.Object, profile, NullLogger<SimulatedCpuLoadFrameStacker>.Instance);

        stacker.OnConfigurationChanged(previous, current);

        inner.As<IFrameStackerConfigurationListener>().Verify(l => l.OnConfigurationChanged(previous, current), Times.Once);
    }

    private static CameraConfiguration CreateConfiguration()
        => new(
            EnableStacking: true,
            StackingFrameCount: 2,
            EnableImageOverlays: false,
            EnableCircularApertureMask: false,
            StackingBufferMinimumFrames: 2,
            StackingBufferIntegrationSeconds: 0,
            FrameFilters: Array.Empty<string>(),
            ProcessedImageEncoding: new ImageEncodingSettings());

    private static void DisposeFrameResult(FrameStackResult result)
    {
        result.StackedImage.Dispose();
        if (!ReferenceEquals(result.StackedImage, result.OriginalImage))
        {
            result.OriginalImage.Dispose();
        }
    }
}
