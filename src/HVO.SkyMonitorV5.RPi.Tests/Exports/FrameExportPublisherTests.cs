using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;
using HVO.SkyMonitorV5.RPi.Models;
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

        var publisher = new FrameExportPublisher(dispatcher.Object, NullLogger<FrameExportPublisher>.Instance);

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

        var descriptor = envelope.Metadata.RawImageDescriptor!;
        Assert.AreEqual(16, descriptor.Width, "Descriptor width should match the captured image.");
        Assert.AreEqual(16, descriptor.Height, "Descriptor height should match the captured image.");
        Assert.AreEqual(descriptor.RowBytes * descriptor.Height, envelope.Payload.Length, "Payload length should match descriptor row bytes.");
        Assert.IsTrue(descriptor.GammaIsLinear, "Descriptor should note linear gamma for the capture.");
    }
}
