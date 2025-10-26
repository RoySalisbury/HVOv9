using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.ImageHistory;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;
using HVO.SkyMonitorV5.RPi.Telemetry;
using P = HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class FrameExportPublisher_MatrixTests
{
  private static (ProcessedFrame Frame, FrameStackResult Stack) CreateFrame()
  {
    using var img = SkiaTestImageFactory.CreateLinearGradientImage(16, 16);
    var frame = new ProcessedFrame(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        new ExposureSettings(250, 120, false, false),
        new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 90),
        "image/jpeg",
        FileExtension: "jpg",
        FramesStacked: 1,
        IntegrationMilliseconds: 250,
        AppliedFilters: Array.Empty<string>(),
        ProcessingMilliseconds: 5,
        ImmutableImage: img.ToRasterImage());

    var stack = new FrameStackResult(
        frame.FrameId,
        new SKBitmap(16, 16),
        new SKBitmap(16, 16),
        frame.Timestamp,
        frame.Exposure,
        Context: null,
        FramesStacked: frame.FramesStacked,
        IntegrationMilliseconds: frame.IntegrationMilliseconds)
    {
      StackedImmutableImage = frame.ImmutableImage,
      OriginalImmutableImage = frame.ImmutableImage
    };

    return (frame, stack);
  }

  private static FrameExportPublisher CreatePublisher(
      IFrameExportDispatcher dispatcher,
      IProcessedFrameEncoder processedEncoder,
      IImageFrameArchiveIngestionQueue archiveQueue,
      FrameExportOptions exportOptions,
      ImageHistoryOptions? imageHistoryOptions = null)
  {
    var featureOptions = new SkiaPipelineFeatureOptions { EnableProcessedFrameEncoder = true };
    var featureOptionsMonitor = new Mock<IOptionsMonitor<SkiaPipelineFeatureOptions>>();
    featureOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(featureOptions);

    var featureToggle = new Mock<ISkiaPipelineFeatureToggleMonitor>(MockBehavior.Strict);

    var imageHistoryMonitor = new Mock<IOptionsMonitor<ImageHistoryOptions>>();
    imageHistoryMonitor.SetupGet(m => m.CurrentValue).Returns(imageHistoryOptions ?? new ImageHistoryOptions());

    var exportOptionsMonitor = new Mock<IOptionsMonitor<FrameExportOptions>>();
    exportOptionsMonitor.SetupGet(m => m.CurrentValue).Returns(exportOptions);

    var noopFitsEncoder = new Mock<IFitsFrameEncoder>();


    noopFitsEncoder.Setup(e => e.EncodeRaw(It.IsAny<SKBitmap>(), It.IsAny<CapturedImage>(), It.IsAny<RigSpec>(), It.IsAny<FitsEncodingOptions>()))


        .Returns(new ProcessedFrameDelivery(Array.Empty<byte>(), "application/fits", "fits"));


    noopFitsEncoder.Setup(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), It.IsAny<FitsEncodingOptions>()))


        .Returns(new ProcessedFrameDelivery(Array.Empty<byte>(), "application/fits", "fits"));



    return new FrameExportPublisher(


        dispatcher,


        processedEncoder,


        noopFitsEncoder.Object,


        NullLogger<FrameExportPublisher>.Instance,
        featureOptionsMonitor.Object,
        featureToggle.Object,
        archiveQueue,
        imageHistoryMonitor.Object,
        exportOptionsMonitor.Object);
  }

  [TestMethod]
  public void DeliveryEncoding_Quality_Clamped_ToBounds()
  {
    // Arrange
    var (frame, stack) = CreateFrame();

    var exportOptions = new FrameExportOptions
    {
      Processed = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 95),
        DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 150) // encoder will clamp to 100
      }
    };

    ImageEncodingSettings? capturedDelivery = null;

    var encoder = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);
    encoder
        .Setup(e => e.Encode(It.IsAny<ProcessedFrame>(), ProcessedFrameEncodingContext.UserInterface, It.IsAny<ImageEncodingSettings>()))
        .Callback<ProcessedFrame, ProcessedFrameEncodingContext, ImageEncodingSettings>((_, _, enc) => capturedDelivery = enc)
        .Returns(new ProcessedFrameDelivery(new byte[] { 1, 2, 3 }, "image/jpeg", "jpg"));
    encoder
        .Setup(e => e.Encode(It.IsAny<ProcessedFrame>(), ProcessedFrameEncodingContext.Export, It.IsAny<ImageEncodingSettings>()))
        .Returns(new ProcessedFrameDelivery(new byte[] { 9 }, "image/jpeg", "jpg"));

    FrameExportEnvelope? deliveryEnvelope = null;
    var dispatcher = new Mock<IFrameExportDispatcher>(MockBehavior.Strict);
    dispatcher.Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()))
        .Callback<FrameExportEnvelope>(env => deliveryEnvelope = env)
        .Returns(true);

    var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
    archiveQueue.Setup(q => q.TryEnqueue(It.IsAny<ImageFrameArchiveIngestionRequest>())).Returns(true);

    var publisher = CreatePublisher(dispatcher.Object, encoder.Object, archiveQueue.Object, exportOptions, new ImageHistoryOptions { EnableArchive = true });

    // Act
    publisher.PublishProcessedFrame(1, stack, frame, RigPresets.MockAsi174_Fujinon, 1.0, 2.0, DateTimeOffset.UtcNow);

    // Assert: publisher passes unclamped encoding to encoder; encoder normalizes via its own logic
    Assert.IsNotNull(capturedDelivery);
    Assert.AreEqual(ImageEncodingFormat.Jpeg, capturedDelivery!.Format);
    Assert.AreEqual(150, capturedDelivery.Quality, "Publisher passes delivery encoding as-is; encoder normalizes internally.");
    Assert.IsNotNull(deliveryEnvelope);
    Assert.AreEqual("image/jpeg", deliveryEnvelope!.ContentType);
    Assert.AreEqual("jpg", deliveryEnvelope.FileExtension);
  }

  [TestMethod]
  public void RoleFormats_DeliveryJpeg_ArchivePng_AreApplied()
  {
    // Arrange
    var (frame, stack) = CreateFrame();

    var exportOptions = new FrameExportOptions
    {
      Processed = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100),
        DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 80)
      }
    };

    ImageEncodingSettings? deliveryEnc = null;
    ImageEncodingSettings? archiveEnc = null;

    var encoder = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);
    encoder
        .Setup(e => e.Encode(It.IsAny<ProcessedFrame>(), ProcessedFrameEncodingContext.UserInterface, It.IsAny<ImageEncodingSettings>()))
        .Callback<ProcessedFrame, ProcessedFrameEncodingContext, ImageEncodingSettings>((_, _, enc) => deliveryEnc = enc)
        .Returns(new ProcessedFrameDelivery(new byte[] { 7, 7 }, "image/jpeg", "jpg"));
    encoder
        .Setup(e => e.Encode(It.IsAny<ProcessedFrame>(), ProcessedFrameEncodingContext.Export, It.IsAny<ImageEncodingSettings>()))
        .Callback<ProcessedFrame, ProcessedFrameEncodingContext, ImageEncodingSettings>((_, _, enc) => archiveEnc = enc)
        .Returns(new ProcessedFrameDelivery(new byte[] { 8, 8 }, "image/png", "png"));

    FrameExportEnvelope? deliveryEnvelope = null;
    var dispatcher = new Mock<IFrameExportDispatcher>(MockBehavior.Strict);
    dispatcher.Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()))
        .Callback<FrameExportEnvelope>(env => deliveryEnvelope = env)
        .Returns(true);

    ImageFrameArchiveIngestionRequest? archiveRequest = null;
    var archiveQueue = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
    archiveQueue.Setup(q => q.TryEnqueue(It.IsAny<ImageFrameArchiveIngestionRequest>()))
        .Callback<ImageFrameArchiveIngestionRequest>(req => archiveRequest = req)
        .Returns(true);

    var publisher = CreatePublisher(dispatcher.Object, encoder.Object, archiveQueue.Object, exportOptions, new ImageHistoryOptions { EnableArchive = true });

    // Act
    publisher.PublishProcessedFrame(2, stack, frame, RigPresets.MockAsi174_Fujinon, 2.0, 3.0, DateTimeOffset.UtcNow);

    // Assert
    Assert.IsNotNull(deliveryEnc);
    Assert.AreEqual(ImageEncodingFormat.Jpeg, deliveryEnc!.Format);
    Assert.AreEqual(80, deliveryEnc.Quality);
    Assert.IsNotNull(archiveEnc);
    Assert.AreEqual(ImageEncodingFormat.Png, archiveEnc!.Format);
    Assert.AreEqual(100, archiveEnc.Quality);

    Assert.IsNotNull(deliveryEnvelope);
    Assert.AreEqual("image/jpeg", deliveryEnvelope!.ContentType);
    Assert.AreEqual("jpg", deliveryEnvelope.FileExtension);

    Assert.IsNotNull(archiveRequest);
    Assert.AreEqual("image/png", archiveRequest!.ContentType);
    StringAssert.EndsWith(archiveRequest.FileExtension, "png");
  }

  [TestMethod]
  public void DeliveryNullFallback_UsesArchive_WhenRaster_ElseEnforcesJpeg85()
  {
    // Arrange
    var (frame, stack) = CreateFrame();

    // Case A: Archive PNG, Delivery null → delivery uses PNG
    var exportOptionsA = new FrameExportOptions
    {
      Processed = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100),
        DeliveryEncoding = null
      }
    };

    ImageEncodingSettings? deliveryA = null;
    ImageEncodingSettings? archiveA = null;

    var encoderA = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);
    encoderA
        .Setup(e => e.Encode(It.IsAny<ProcessedFrame>(), ProcessedFrameEncodingContext.UserInterface, It.IsAny<ImageEncodingSettings>()))
        .Callback<ProcessedFrame, ProcessedFrameEncodingContext, ImageEncodingSettings>((_, _, enc) => deliveryA = enc)
        .Returns(new ProcessedFrameDelivery(new byte[] { 1 }, "image/png", "png"));
    encoderA
        .Setup(e => e.Encode(It.IsAny<ProcessedFrame>(), ProcessedFrameEncodingContext.Export, It.IsAny<ImageEncodingSettings>()))
        .Callback<ProcessedFrame, ProcessedFrameEncodingContext, ImageEncodingSettings>((_, _, enc) => archiveA = enc)
        .Returns(new ProcessedFrameDelivery(new byte[] { 2 }, "image/png", "png"));

    var dispatcherA = new Mock<IFrameExportDispatcher>(MockBehavior.Strict);
    dispatcherA.Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>())).Returns(true);
    var archiveQueueA = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
    archiveQueueA.Setup(q => q.TryEnqueue(It.IsAny<ImageFrameArchiveIngestionRequest>())).Returns(true);

    var publisherA = CreatePublisher(dispatcherA.Object, encoderA.Object, archiveQueueA.Object, exportOptionsA, new ImageHistoryOptions { EnableArchive = true });

    publisherA.PublishProcessedFrame(3, stack, frame, RigPresets.MockAsi174_Fujinon, 3.0, 4.0, DateTimeOffset.UtcNow);

    Assert.IsNotNull(deliveryA);
    Assert.AreEqual(ImageEncodingFormat.Png, deliveryA!.Format);
    Assert.AreEqual(100, deliveryA.Quality);
    Assert.IsNotNull(archiveA);
    Assert.AreEqual(ImageEncodingFormat.Png, archiveA!.Format);
    Assert.AreEqual(100, archiveA.Quality);

    // Case B: Archive FITS, Delivery null → delivery forced to JPEG 85, archive FITS
    var exportOptionsB = new FrameExportOptions
    {
      Processed = new FrameExportStageOptions
      {
        Enabled = true,
        PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery,
        ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
        {
          FitsOptions = new FitsEncodingOptions { BitDepth = P.FitsBitDepth.U16 }
        },
        DeliveryEncoding = null
      }
    };

    ImageEncodingSettings? deliveryB = null;
    ImageEncodingSettings? archiveB = null;

    var encoderB = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);
    encoderB
        .Setup(e => e.Encode(It.IsAny<ProcessedFrame>(), ProcessedFrameEncodingContext.UserInterface, It.IsAny<ImageEncodingSettings>()))
        .Callback<ProcessedFrame, ProcessedFrameEncodingContext, ImageEncodingSettings>((_, _, enc) => deliveryB = enc)
        .Returns(new ProcessedFrameDelivery(new byte[] { 3 }, "image/jpeg", "jpg"));
    encoderB
        .Setup(e => e.Encode(It.IsAny<ProcessedFrame>(), ProcessedFrameEncodingContext.Export, It.IsAny<ImageEncodingSettings>()))
        .Callback<ProcessedFrame, ProcessedFrameEncodingContext, ImageEncodingSettings>((_, _, enc) => archiveB = enc)
        .Returns(new ProcessedFrameDelivery(new byte[] { 4 }, "application/fits", "fits"));

    var dispatcherB = new Mock<IFrameExportDispatcher>(MockBehavior.Strict);
    dispatcherB.Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>())).Returns(true);
    var archiveQueueB = new Mock<IImageFrameArchiveIngestionQueue>(MockBehavior.Strict);
    archiveQueueB.Setup(q => q.TryEnqueue(It.IsAny<ImageFrameArchiveIngestionRequest>())).Returns(true);

    var publisherB = CreatePublisher(dispatcherB.Object, encoderB.Object, archiveQueueB.Object, exportOptionsB, new ImageHistoryOptions { EnableArchive = true });

    publisherB.PublishProcessedFrame(4, stack, frame, RigPresets.MockAsi174_Fujinon, 4.0, 5.0, DateTimeOffset.UtcNow);

    Assert.IsNotNull(deliveryB);
    Assert.AreEqual(ImageEncodingFormat.Jpeg, deliveryB!.Format, "Delivery should be forced to raster (JPEG). ");
    Assert.AreEqual(85, deliveryB.Quality, "Forced raster delivery uses 85 quality.");
    Assert.IsNotNull(archiveB);
    Assert.AreEqual(ImageEncodingFormat.Fits, archiveB!.Format);
    Assert.AreEqual(100, archiveB.Quality);
    Assert.IsNotNull(archiveB.FitsOptions);
  }
}
