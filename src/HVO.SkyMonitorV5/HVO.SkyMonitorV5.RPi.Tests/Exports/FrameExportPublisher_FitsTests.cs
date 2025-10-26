using System;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class FrameExportPublisherFitsTests
{
    [TestMethod]
    public void PublishProcessedFrame_FitsEnabled_UsesPngExportEnvelope()
    {
        // Arrange
        var dispatcher = new Mock<IFrameExportDispatcher>(MockBehavior.Strict);
        FrameExportEnvelope? capturedEnvelope = null;
        dispatcher
            .Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()))
            .Callback<FrameExportEnvelope>(envelope => capturedEnvelope = envelope)
            .Returns(true);

        var fitsOptions = new FitsExportOptions { EnableForProcessed = true };
        var fitsOptionsMonitor = new Mock<IOptionsMonitor<FitsExportOptions>>();
        fitsOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(fitsOptions);

        var expectedFitsBytes = new byte[] { 70, 73, 84, 83 }; // 'FITS' header bytes for test
        var fitsEncoderMock = new Mock<IFitsFrameEncoder>(MockBehavior.Strict);
        fitsEncoderMock
            .Setup(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), fitsOptions))
            .Returns(new ProcessedFrameDelivery(expectedFitsBytes, "application/fits", "fits"));

        var rigAdapter = new Mock<IRigAcquisitionAdapter>(MockBehavior.Loose);
        rigAdapter.SetupGet(a => a.ActiveRig).Returns(RigPresets.MockAsi174_Fujinon);

        var processedEncoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoderMock.Object,
            rigAdapter.Object,
            fitsOptionsMonitor.Object
        );

        var featureOptions = new SkiaPipelineFeatureOptions { EnableProcessedFrameEncoder = true };
        var featureOptionsMonitor = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
        featureOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(featureOptions);

        var featureMonitor = new Mock<ISkiaPipelineFeatureToggleMonitor>(MockBehavior.Strict);
        var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
        var imageHistoryMonitor = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
        imageHistoryMonitor.SetupGet(m => m.CurrentValue).Returns(new ImageHistoryOptions { EnableArchive = false });

        using var skImage = SKImage.Create(new SKImageInfo(8, 8));
        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            new ExposureSettings(500, 120, false, false),
            new ImageEncodingSettings(ImageEncodingFormat.Png, 95),
            "image/png",
            null,
            1,
            500,
            Array.Empty<string>(),
            10,
            skImage
        );

        using var stacked = new SKBitmap(8, 8);
        using var original = new SKBitmap(8, 8);
        var stackResult = new FrameStackResult(
            frame.FrameId,
            stacked,
            original,
            frame.Timestamp,
            frame.Exposure,
            Context: null,
            FramesStacked: frame.FramesStacked,
            IntegrationMilliseconds: frame.IntegrationMilliseconds)
        {
            StackedImmutableImage = frame.ImmutableImage,
            OriginalImmutableImage = frame.ImmutableImage
        };

        var publisher = new FrameExportPublisher(
            dispatcher.Object,
            processedEncoder,
            fitsEncoderMock.Object,
            NullLogger<FrameExportPublisher>.Instance,
            featureOptionsMonitor.Object,
            featureMonitor.Object,
            archiveQueue.Object,
            imageHistoryMonitor.Object,
            fitsOptionsMonitor.Object
        );

        // Act
        publisher.PublishProcessedFrame(
            frameNumber: 1,
            stackResult,
            frame,
            RigPresets.MockAsi174_Fujinon,
            queueLatencyMilliseconds: 1.0,
            processingMilliseconds: 2.0,
            stageTimestampUtc: DateTimeOffset.UtcNow
        );

        // Assert
        dispatcher.Verify(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()), Times.Once);
        Assert.IsNotNull(capturedEnvelope);
        Assert.AreEqual("image/png", capturedEnvelope!.ContentType, "Processed exports should use PNG/JPG, not FITS");
        Assert.AreEqual("png", capturedEnvelope.FileExtension);
        Assert.IsGreaterThan(0, capturedEnvelope.Payload.Length);
        // FITS encoder should not have been used for processed exports
    }
}
