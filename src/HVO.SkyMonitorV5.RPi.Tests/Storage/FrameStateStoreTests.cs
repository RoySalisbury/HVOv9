using System;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
using HVO.SkyMonitorV5.RPi.Storage;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Storage;

[TestClass]
public sealed class FrameStateStoreTests
{
    [TestMethod]
    public void UpdateFrame_EnqueuesComposedFrameHistory()
    {
        using var optionsMonitor = new TestOptionsMonitor<CameraPipelineOptions>(new CameraPipelineOptions());
        var clockMock = new Mock<IObservatoryClock>();
        var now = DateTimeOffset.UtcNow;

        clockMock.SetupGet(clock => clock.UtcNow).Returns(now);
        clockMock.SetupGet(clock => clock.LocalNow).Returns(now);
        clockMock.SetupGet(clock => clock.TimeZone).Returns(TimeZoneInfo.Utc);
        clockMock.SetupGet(clock => clock.TimeZoneDisplayName).Returns("UTC");
        clockMock.Setup(clock => clock.GetZoneLabel(It.IsAny<DateTimeOffset>())).Returns("UTC");
        clockMock.Setup(clock => clock.ToLocal(It.IsAny<DateTimeOffset>())).Returns<DateTimeOffset>(value => value);

        using var store = new FrameStateStore(optionsMonitor, clockMock.Object, NullLogger<FrameStateStore>.Instance);

        var exposure = new ExposureSettings(ExposureMilliseconds: 1000, Gain: 200, AutoExposure: false, AutoGain: false);
        var frameId = Guid.NewGuid();

        var rawBitmap = new SKBitmap(width: 4, height: 4);
        var rawImage = SKImage.FromBitmap(rawBitmap);
        var rawSnapshot = new RawFrameSnapshot(frameId, rawBitmap, now, exposure)
        {
            ImmutableImage = rawImage
        };

        var processedBitmap = new SKBitmap(width: 4, height: 4);
        var processedImage = SKImage.FromBitmap(processedBitmap);
        var encoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 90);
        var processedFrame = new ProcessedFrame(
            frameId,
            now,
            exposure,
            encoding,
            ImageEncodingUtilities.ToContentType(encoding.Format),
            ImageEncodingUtilities.ToFileExtension(encoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: new[] { "TestFilter" },
            ProcessingMilliseconds: 12,
            ImmutableImage: processedImage)
        {
            FilterExecutions = new[] { new FilterExecution("TestFilter", 1.25) },
            SurfaceMilliseconds = 0.5
        };
        processedBitmap.Dispose();

        store.UpdateFrame(rawSnapshot, processedFrame);

        var history = store.GetComposedFrameHistory();
        Assert.AreEqual(1, history.Count, "History should contain the most recent composition.");

        var composed = history[0];
        Assert.AreEqual(frameId, composed.FrameId, "Frame IDs should match the processed frame.");
        Assert.AreEqual(now, composed.Timestamp, "Timestamp should reflect localized capture time.");
        Assert.AreEqual(1, composed.AppliedFilters.Count, "Applied filters should be preserved.");
        Assert.AreEqual("TestFilter", composed.AppliedFilters[0], "Filter order should be stable.");
        Assert.AreEqual(1, composed.FilterExecutions.Count, "Filter execution metadata should be present.");
    Assert.AreEqual(0.5, composed.SurfaceMilliseconds, 1e-6, "Surface preparation timing should be captured.");
        Assert.AreNotSame(processedImage, composed.Image, "History should own an independent SKImage instance.");
        Assert.IsTrue(composed.Image.Width > 0 && composed.Image.Height > 0, "Snapshot should contain pixel data.");
    }
}
