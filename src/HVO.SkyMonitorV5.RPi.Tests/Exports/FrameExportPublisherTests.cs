using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Exports;

[TestClass]
public sealed class FrameExportPublisherTests
{
    [TestMethod]
    public void PublishRawFrame_EnqueuesHighBitPayload()
    {
        var dispatcher = new Mock<IFrameExportDispatcher>(MockBehavior.Strict);
        FrameExportEnvelope? capturedEnvelope = null;
        dispatcher
            .Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()))
            .Callback<FrameExportEnvelope>(envelope => capturedEnvelope = envelope)
            .Returns(true);

        var encoder = new ProcessedFrameEncoder(NullLogger<ProcessedFrameEncoder>.Instance);
        var publisher = new FrameExportPublisher(dispatcher.Object, encoder, NullLogger<FrameExportPublisher>.Instance);

        var exposure = new ExposureSettings(ExposureMilliseconds: 500, Gain: 120, AutoExposure: false, AutoGain: false);
        var info = new SKImageInfo(16, 16, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());

        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(new SKColorF(0.2f, 0.4f, 0.6f, 1f));
        using var immutableImage = surface.Snapshot();
        using var bitmap = new SKBitmap(info);
        immutableImage.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes);

        var capture = new CapturedImage(
            Guid.NewGuid(),
            bitmap,
            DateTimeOffset.UtcNow,
            exposure,
            Context: null)
        {
            ImmutableImage = immutableImage
        };

        var rig = RigPresets.MockAsi174_Fujinon;
        var stageTimestampUtc = DateTimeOffset.UtcNow;

        publisher.PublishRawFrame(
            frameNumber: 1,
            capture,
            rig,
            captureMilliseconds: 8.5,
            stageTimestampUtc: stageTimestampUtc);

        dispatcher.Verify(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()), Times.Once);
        Assert.IsNotNull(capturedEnvelope, "Publisher should enqueue a frame export envelope.");

        var envelope = capturedEnvelope!;
        Assert.AreEqual("application/vnd.hvo.skia.raw", envelope.ContentType, "Raw exports should use the high-bit content type.");
        Assert.AreEqual("skimg", envelope.FileExtension, "Raw exports should use the skimg extension.");
        Assert.IsNotNull(envelope.Metadata.RawImageDescriptor, "Raw exports should include an image descriptor.");
    Assert.AreEqual(SkiaRawFrameHelper.RawContentType, envelope.Metadata.PayloadContentType, "Metadata should advertise raw payload content type.");
    Assert.AreEqual(SkiaRawFrameHelper.RawFileExtension, envelope.Metadata.PayloadExtension, "Metadata should advertise raw payload extension.");

        var descriptor = envelope.Metadata.RawImageDescriptor!;
        Assert.AreEqual(16, descriptor.Width, "Descriptor width should match the captured image.");
        Assert.AreEqual(16, descriptor.Height, "Descriptor height should match the captured image.");
        Assert.AreEqual(descriptor.RowBytes * descriptor.Height, envelope.Payload.Length, "Payload length should match descriptor row bytes.");
        Assert.IsTrue(descriptor.GammaIsLinear, "Descriptor should note linear gamma for the capture.");
    }

    [TestMethod]
    public void PublishProcessedFrame_UsesEncoderDeliveryMetadata()
    {
        var dispatcher = new Mock<IFrameExportDispatcher>(MockBehavior.Strict);
        FrameExportEnvelope? capturedEnvelope = null;
        dispatcher
            .Setup(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()))
            .Callback<FrameExportEnvelope>(envelope => capturedEnvelope = envelope)
            .Returns(true);

        var encoder = new Mock<IProcessedFrameEncoder>(MockBehavior.Strict);

        using var surface = SKSurface.Create(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.MidnightBlue);
        using var immutableImage = surface.Snapshot();

        var payload = new byte[] { 1, 2, 3, 4 };
        var delivery = new ProcessedFrameDelivery(payload, "image/png", "png");
        encoder
            .Setup(e => e.Encode(It.IsAny<ProcessedFrame>()))
            .Returns(delivery);

        var publisher = new FrameExportPublisher(dispatcher.Object, encoder.Object, NullLogger<FrameExportPublisher>.Instance);

        using var stackedBitmap = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var originalBitmap = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);

        var exposure = new ExposureSettings(ExposureMilliseconds: 750, Gain: 180, AutoExposure: false, AutoGain: false);
        var processedFrame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            new ImageEncodingSettings(ImageEncodingFormat.Png, 95),
            "image/png",
            FileExtension: null,
            FramesStacked: 3,
            IntegrationMilliseconds: 2250,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 12,
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

        var rig = RigPresets.MockAsi174_Fujinon;
        var stageTimestampUtc = DateTimeOffset.UtcNow;

        publisher.PublishProcessedFrame(
            frameNumber: 42,
            stackResult,
            processedFrame,
            rig,
            queueLatencyMilliseconds: 4.2,
            processingMilliseconds: 12.6,
            stageTimestampUtc: stageTimestampUtc);

        dispatcher.Verify(d => d.TryEnqueue(It.IsAny<FrameExportEnvelope>()), Times.Once);
        encoder.Verify(e => e.Encode(It.Is<ProcessedFrame>(frame => frame.FrameId == processedFrame.FrameId)), Times.Once);

    Assert.IsNotNull(capturedEnvelope, "Processed frame publish should enqueue an export envelope.");
    var envelope = capturedEnvelope!;

        CollectionAssert.AreEqual(payload, envelope.Payload.ToArray(), "Envelope payload should originate from the encoder delivery.");
        Assert.AreEqual("image/png", envelope.ContentType, "Envelope should advertise encoder content type.");
        Assert.AreEqual("png", envelope.FileExtension, "Envelope should use encoder file extension.");
    Assert.AreEqual("image/png", envelope.Metadata.PayloadContentType, "Metadata should track processed payload content type.");
    Assert.AreEqual("png", envelope.Metadata.PayloadExtension, "Metadata should track processed payload extension.");

        Assert.AreEqual(processedFrame.FrameId, envelope.FrameId, "Envelope should preserve processed frame identifier.");
        Assert.AreEqual(FrameExportStage.Processed, envelope.Stage, "Processed frame should target the processed stage.");
        Assert.AreEqual(processedFrame.FramesStacked, envelope.Metadata.FramesStacked, "Metadata should reflect processed frame stacking information.");
    }
}
