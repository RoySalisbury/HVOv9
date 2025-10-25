using System;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports
{
    [TestClass]
    public sealed class FrameExportPublisherTests
    {
        [TestMethod]
        public void PublishProcessedFrame_FitsEnabled_EnqueuesFITSExportEnvelope()
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

            var processedEncoder = new ProcessedFrameEncoder(
                NullLogger<ProcessedFrameEncoder>.Instance,
                fitsEncoderMock.Object,
                new Mock<IRigAcquisitionAdapter>().Object,
                fitsOptionsMonitor.Object
            );

            var featureOptions = new SkiaPipelineFeatureOptions { EnableProcessedFrameEncoder = true };
            var featureOptionsMonitor = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
            featureOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(featureOptions);

            var monitor = new Mock<ISkiaPipelineFeatureToggleMonitor>();
            var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
            var imageHistoryMonitor = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
            imageHistoryMonitor.SetupGet(m => m.CurrentValue).Returns(new ImageHistoryOptions());

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
                SKImage.Create(new SKImageInfo(8, 8))
            );

            var stackResult = new FrameStackResult(
                frame.FrameId,
                new SKBitmap(8, 8),
                new SKBitmap(8, 8),
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
                monitor.Object,
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
            Assert.AreEqual("application/fits", capturedEnvelope.ContentType);
            Assert.AreEqual("fits", capturedEnvelope.FileExtension);
            Assert.IsTrue(capturedEnvelope.Payload.Length > 0);
            CollectionAssert.AreEqual(expectedFitsBytes, capturedEnvelope.Payload.ToArray());
        }
    }
}
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports
    }
}
public sealed class FrameExportPublisherTests
{
    [TestMethod]
    public void PublishProcessedFrame_FitsEnabled_EnqueuesFITSExportEnvelope()
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

        var processedEncoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoderMock.Object,
            new Mock<IRigAcquisitionAdapter>().Object,
            fitsOptionsMonitor.Object
        );

        var featureOptions = new SkiaPipelineFeatureOptions { EnableProcessedFrameEncoder = true };
        var featureOptionsMonitor = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
        featureOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(featureOptions);

        // ISkiaPipelineFeatureToggleMonitor is likely an interface in the pipeline or services namespace
        // If missing, ensure the correct using is present or mock as needed
        var monitor = new Mock<ISkiaPipelineFeatureToggleMonitor>();
        var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
        var imageHistoryMonitor = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
        imageHistoryMonitor.SetupGet(m => m.CurrentValue).Returns(new ImageHistoryOptions());

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
            SKImage.Create(new SKImageInfo(8, 8))
        );

        var stackResult = new FrameStackResult(
            frame.FrameId,
            new SKBitmap(8, 8),
            new SKBitmap(8, 8),
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
            monitor.Object,
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
        Assert.AreEqual("application/fits", capturedEnvelope.ContentType);
        Assert.AreEqual("fits", capturedEnvelope.FileExtension);
        Assert.IsTrue(capturedEnvelope.Payload.Length > 0);
        CollectionAssert.AreEqual(expectedFitsBytes, capturedEnvelope.Payload.ToArray());
    }
}
}
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports
{
    [TestClass]
    public sealed class FrameExportPublisherTests
    {
        [TestMethod]
        public void PublishProcessedFrame_FitsEnabled_EnqueuesFITSExportEnvelope()
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

            var processedEncoder = new ProcessedFrameEncoder(
                NullLogger<ProcessedFrameEncoder>.Instance,
                fitsEncoderMock.Object,
                new Mock<IRigAcquisitionAdapter>().Object,
                fitsOptionsMonitor.Object
            );

            var featureOptions = new SkiaPipelineFeatureOptions { EnableProcessedFrameEncoder = true };
            var featureOptionsMonitor = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
            featureOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(featureOptions);

            var monitor = new Mock<ISkiaPipelineFeatureToggleMonitor>(MockBehavior.Strict);
            var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
            var imageHistoryMonitor = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
            imageHistoryMonitor.SetupGet(m => m.CurrentValue).Returns(new ImageHistoryOptions());

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
                SKImage.Create(new SKImageInfo(8, 8))
            );

            var stackResult = new FrameStackResult(
                frame.FrameId,
                new SKBitmap(8, 8),
                new SKBitmap(8, 8),
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
                monitor.Object,
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
            Assert.AreEqual("application/fits", capturedEnvelope.ContentType);
            Assert.AreEqual("fits", capturedEnvelope.FileExtension);
            Assert.IsTrue(capturedEnvelope.Payload.Length > 0);
            CollectionAssert.AreEqual(expectedFitsBytes, capturedEnvelope.Payload.ToArray());
        }
    }
}
// Truncate after the closing brace of the class
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Pipeline;
using System;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;
[TestClass]
public sealed class FrameExportPublisherTests
    using System;
    using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
    using HVO.SkyMonitorV5.RPi.Cameras.Projection;
    using HVO.SkyMonitorV5.RPi.Exports;
    using HVO.SkyMonitorV5.RPi.ImageHistory;
    using HVO.SkyMonitorV5.RPi.Models;
    using HVO.SkyMonitorV5.RPi.Options;
    using Microsoft.Extensions.Logging.Abstractions;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using SkiaSharp;

    namespace HVO.SkyMonitorV5.RPi.Tests.Exports
{
    [TestClass]
    public sealed class FrameExportPublisherTests
    {
        [TestMethod]
        public void PublishProcessedFrame_FitsEnabled_EnqueuesFITSExportEnvelope()
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

            var processedEncoder = new ProcessedFrameEncoder(
                NullLogger<ProcessedFrameEncoder>.Instance,
                fitsEncoderMock.Object,
                new Mock<IRigAcquisitionAdapter>().Object,
                fitsOptionsMonitor.Object
            );

            var featureOptions = new SkiaPipelineFeatureOptions { EnableProcessedFrameEncoder = true };
            var featureOptionsMonitor = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
            featureOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(featureOptions);

            var monitor = new Mock<ISkiaPipelineFeatureToggleMonitor>(MockBehavior.Strict);
            var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
            var imageHistoryMonitor = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
            imageHistoryMonitor.SetupGet(m => m.CurrentValue).Returns(new ImageHistoryOptions());

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
                SKImage.Create(new SKImageInfo(8, 8))
            );

            var stackResult = new FrameStackResult(
                frame.FrameId,
                new SKBitmap(8, 8),
                new SKBitmap(8, 8),
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
                monitor.Object,
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
            Assert.AreEqual("application/fits", capturedEnvelope.ContentType);
            Assert.AreEqual("fits", capturedEnvelope.FileExtension);
            Assert.IsTrue(capturedEnvelope.Payload.Length > 0);
            CollectionAssert.AreEqual(expectedFitsBytes, capturedEnvelope.Payload.ToArray());
        }
    }
}
DateTimeOffset.UtcNow,
            exposure,
            new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 90),
            "image/jpeg",
            FileExtension: "jpg",
            FramesStacked: 1,
            IntegrationMilliseconds: 500,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 10,
            ImmutableImage: immutableImage);

var stackResult = new FrameStackResult(
    processedFrame.FrameId,
    stackedBitmap,
    originalBitmap,
    processedFrame.Timestamp,
    exposure,
    Context: null,
    FramesStacked: processedFrame.FramesStacked,
    IntegrationMilliseconds: processedFrame.IntegrationMilliseconds)
{
    StackedImmutableImage = immutableImage,
    OriginalImmutableImage = immutableImage
};

publisher.PublishProcessedFrame(
    frameNumber: 99,
    stackResult,
    processedFrame,
    RigPresets.MockAsi174_Fujinon,
    queueLatencyMilliseconds: 2.5,
    processingMilliseconds: 10.0,
    stageTimestampUtc: DateTimeOffset.UtcNow);

queue.Verify(q => q.TryEnqueue(It.Is<ImageFrameArchiveIngestionRequest>(request => request.FrameId == processedFrame.FrameId)), Times.Once);
    }
}

    [TestMethod]
public void PublishProcessedFrame_EncoderDisabledFallsBackToImmutableImage()
{
    var dispatcher = new Mock<IFrameExportDispatcher>(MockBehavior.Strict);
    FrameExportEnvelope? capturedEnvelope = null;
    dispatcher
        .Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()))
        .Callback<FrameExportEnvelope>(envelope => capturedEnvelope = envelope)
        .Returns(true);

    var encoder = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);

    var featureOptions = new SkiaPipelineFeatureOptions
    {
        EnableRawLinearPayloads = true,
        EnableProcessedFrameEncoder = false
    };

    var monitor = new Mock<ISkiaPipelineFeatureToggleMonitor>(MockBehavior.Strict);
    monitor.Setup(m => m.RecordFallback(SkiaPipelineFeatureNames.ProcessedFrameEncoder));

    var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
    archiveQueue.Setup(q => q.TryEnqueue(It.IsAny<ImageFrameArchiveIngestionRequest>())).Returns(true);

    var publisher = CreatePublisher(
        dispatcher.Object,
        encoder.Object,
        featureOptions,
        monitor.Object,
        archiveQueue.Object);

    using var surface = SKSurface.Create(new SKImageInfo(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.DarkSlateBlue);
    using var immutableImage = surface.Snapshot();

    var exposure = new ExposureSettings(ExposureMilliseconds: 500, Gain: 220, AutoExposure: false, AutoGain: false);
    using var stackedBitmap = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var originalBitmap = new SKBitmap(10, 10, SKColorType.Rgba8888, SKAlphaType.Premul);

    var processedFrame = new ProcessedFrame(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        exposure,
        new ImageEncodingSettings(ImageEncodingFormat.Png, 90),
        "image/png",
        FileExtension: null,
        FramesStacked: 2,
        IntegrationMilliseconds: 1_000,
        AppliedFilters: Array.Empty<string>(),
        ProcessingMilliseconds: 5,
        ImmutableImage: immutableImage);

    var stackResult = new FrameStackResult(
        processedFrame.FrameId,
        stackedBitmap,
        originalBitmap,
        processedFrame.Timestamp,
        exposure,
        Context: null,
        FramesStacked: processedFrame.FramesStacked,
        IntegrationMilliseconds: processedFrame.IntegrationMilliseconds)
    {
        StackedImmutableImage = immutableImage,
        OriginalImmutableImage = immutableImage
    };

    publisher.PublishProcessedFrame(
        frameNumber: 12,
        stackResult,
        processedFrame,
        RigPresets.MockAsi174_Fujinon,
        queueLatencyMilliseconds: 1.5,
        processingMilliseconds: 5.0,
        stageTimestampUtc: DateTimeOffset.UtcNow);

    dispatcher.Verify(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()), Times.Once);
    encoder.Verify(e => e.Encode(It.IsAny<ProcessedFrame>()), Times.Never);
    monitor.Verify(m => m.RecordFallback(SkiaPipelineFeatureNames.ProcessedFrameEncoder), Times.Once);

    Assert.IsNotNull(capturedEnvelope, "Fallback path should enqueue envelopes.");
    var envelope = capturedEnvelope!;
    Assert.AreEqual("image/png", envelope.ContentType, "Fallback should encode using PNG to mirror default encoding options.");
    Assert.AreEqual("png", envelope.FileExtension, "Fallback should default to PNG extension.");
}

private static FrameExportPublisher CreatePublisher(
    IFrameExportDispatcher dispatcher,
    IProcessedFrameEncoder encoder,
    SkiaPipelineFeatureOptions options,
    ISkiaPipelineFeatureToggleMonitor monitor,
    IImageFrameArchiveIngestionQueue? archiveQueue = null,
    ImageHistoryOptions? imageHistoryOptions = null)
{
    var optionsMonitor = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
    optionsMonitor.SetupGet(m => m.CurrentValue).Returns(options);

    var imageHistoryMonitor = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
    imageHistoryMonitor.SetupGet(m => m.CurrentValue).Returns(imageHistoryOptions ?? new ImageHistoryOptions());

    var queue = archiveQueue ?? Mock.Of<IImageFrameArchiveIngestionQueue>(q => q.TryEnqueue(It.IsAny<ImageFrameArchiveIngestionRequest>()) == true);

    var fitsOptionsMonitor = new Mock<IOptionsMonitor<FitsExportOptions>>();
    fitsOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(new FitsExportOptions
    {
        EnableForRaw = false,
        EnableForProcessed = false
    });

    return new FrameExportPublisher(
        dispatcher,
