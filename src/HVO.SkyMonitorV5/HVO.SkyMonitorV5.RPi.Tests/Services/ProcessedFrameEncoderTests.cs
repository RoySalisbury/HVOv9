using System;
using System.Security.Cryptography;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using Moq;
using Microsoft.Extensions.Options;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;

#pragma warning disable CS0618 // Suppress obsolete warnings for FitsExportOptions usage in legacy-path tests

namespace HVO.SkyMonitorV5.RPi.Tests.Services;

[TestClass]
public sealed class ProcessedFrameEncoderTests
{
    [TestMethod]
    public void Encode_PngFormat_ReturnsPayloadWithMetadata()
    {
        using var bitmap = new SKBitmap(width: 4, height: 4);
        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 1000, Gain: 200, AutoExposure: false, AutoGain: false);
        var encoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);

        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            encoding,
            ImageEncodingUtilities.ToContentType(encoding.Format),
            ImageEncodingUtilities.ToFileExtension(encoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var fitsOptions = new Mock<IOptionsMonitor<FitsExportOptions>>();
        fitsOptions.SetupGet(o => o.CurrentValue).Returns(new FitsExportOptions { EnableForProcessed = false, EnableForRaw = false });
        var rigAdapter = new Mock<IRigAcquisitionAdapter>();
        rigAdapter.SetupGet(r => r.ActiveRig).Returns(RigPresets.MockAsi174_Fujinon);
        var fitsEncoder = new Mock<IFitsFrameEncoder>();

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder.Object,
            rigAdapter.Object,
            fitsOptions.Object);

        var delivery = encoder.Encode(frame);

        Assert.AreEqual("image/png", delivery.ContentType, "PNG encoding should advertise image/png content type.");
        Assert.AreEqual("png", delivery.FileExtension, "PNG delivery should use png file extension.");
        Assert.IsGreaterThan(0, delivery.Payload.Length, "Encoder should emit non-empty payload.");
    }

    [TestMethod]
    public void Encode_PngFormat_ProducesDeterministicPayloadHash()
    {
        var info = new SKImageInfo(width: 4, height: 4, colorType: SKColorType.Rgba8888, alphaType: SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);

        for (var y = 0; y < info.Height; y++)
        {
            for (var x = 0; x < info.Width; x++)
            {
                var red = (byte)((x + 1) * 32);
                var green = (byte)((y + 1) * 48);
                var blue = (byte)(((x + y) + 1) * 40);
                bitmap.SetPixel(x, y, new SKColor(red, green, blue, 255));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 1000, Gain: 200, AutoExposure: false, AutoGain: false);
        var encoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);

        var frame = new ProcessedFrame(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeffffffff"),
            new DateTimeOffset(2025, 10, 16, 0, 0, 0, TimeSpan.Zero),
            exposure,
            encoding,
            ImageEncodingUtilities.ToContentType(encoding.Format),
            ImageEncodingUtilities.ToFileExtension(encoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var fitsOptions = new Mock<IOptionsMonitor<FitsExportOptions>>();
        fitsOptions.SetupGet(o => o.CurrentValue).Returns(new FitsExportOptions { EnableForProcessed = false, EnableForRaw = false });
        var rigAdapter = new Mock<IRigAcquisitionAdapter>();
        rigAdapter.SetupGet(r => r.ActiveRig).Returns(RigPresets.MockAsi174_Fujinon);
        var fitsEncoder = new Mock<IFitsFrameEncoder>();

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder.Object,
            rigAdapter.Object,
            fitsOptions.Object);

        var delivery = encoder.Encode(frame);

        var hash = SHA256.HashData(delivery.Payload.Span);
        var hashHex = Convert.ToHexString(hash);

        const string expectedHash = "C22A5C3A47ACA7F5A8E2A146E34ED5DDF5D3F68AAB129054A6758C36C225D196";
        Assert.AreEqual(expectedHash, hashHex, "Deterministic PNG encoding should produce the expected hash.");
    }

    [TestMethod]
    public void Encode_FitsEnabled_UsesFitsDeliveryMetadata()
    {
        using var bitmap = new SKBitmap(width: 4, height: 4);
        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 1000, Gain: 200, AutoExposure: false, AutoGain: false);
        var encoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);

        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            encoding,
            ImageEncodingUtilities.ToContentType(encoding.Format),
            ImageEncodingUtilities.ToFileExtension(encoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var fitsPayload = new byte[] { 0x46, 0x49, 0x54, 0x53 }; // 'FITS' marker-like bytes for test

        var fitsOptions = new Mock<IOptionsMonitor<FitsExportOptions>>();
        fitsOptions.SetupGet(o => o.CurrentValue).Returns(new FitsExportOptions { EnableForProcessed = true, EnableForRaw = false });

        var rigAdapter = new Mock<IRigAcquisitionAdapter>(MockBehavior.Strict);
        rigAdapter.SetupGet(r => r.ActiveRig).Returns(RigPresets.MockAsi174_Fujinon);

        var fitsEncoder = new Mock<IFitsFrameEncoder>(MockBehavior.Strict);
        fitsEncoder
            .Setup(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), It.IsAny<FitsExportOptions>()))
            .Returns(new ProcessedFrameDelivery(fitsPayload, "application/fits", "fits"));

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder.Object,
            rigAdapter.Object,
            fitsOptions.Object);

        var delivery = encoder.Encode(frame, ProcessedFrameEncodingContext.Export);

        Assert.AreEqual("application/fits", delivery.ContentType, "FITS-enabled encoding should advertise application/fits content type.");
        Assert.AreEqual("fits", delivery.FileExtension, "FITS-enabled delivery should use fits file extension.");
        Assert.IsGreaterThan(0, delivery.Payload.Length, "FITS-enabled encoder should emit non-empty payload.");

        fitsEncoder.Verify(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), It.IsAny<FitsExportOptions>()), Times.Once);
    }

    [TestMethod]
    public void Encode_FitsEnabledWithUIContext_UsesPngFormat()
    {
        using var bitmap = new SKBitmap(width: 96, height: 96);
        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 100, Gain: 0, AutoExposure: false, AutoGain: false);
        var encoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);

        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            encoding,
            "image/png",
            "png",
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var fitsOptions = new Mock<IOptionsMonitor<FitsExportOptions>>();
        fitsOptions.SetupGet(o => o.CurrentValue).Returns(new FitsExportOptions { EnableForProcessed = true, EnableForRaw = false });

        var rigAdapter = new Mock<IRigAcquisitionAdapter>(MockBehavior.Strict);
        rigAdapter.SetupGet(r => r.ActiveRig).Returns(RigPresets.MockAsi174_Fujinon);

        var fitsEncoder = new Mock<IFitsFrameEncoder>(MockBehavior.Strict);
        // FITS encoder should not be called for UI context

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder.Object,
            rigAdapter.Object,
            fitsOptions.Object);

        var delivery = encoder.Encode(frame, ProcessedFrameEncodingContext.UserInterface);

        Assert.AreEqual("image/png", delivery.ContentType, "UI context should return PNG even when FITS is enabled.");
        Assert.AreEqual("png", delivery.FileExtension, "UI context should use png file extension.");
        Assert.IsGreaterThan(0, delivery.Payload.Length, "UI encoder should emit non-empty payload.");

        // FITS encoder should not have been called for UI context
        fitsEncoder.Verify(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), It.IsAny<FitsExportOptions>()), Times.Never);
    }

    [TestMethod]
    public void Encode_DefaultContext_UsesPngFormat()
    {
        using var bitmap = new SKBitmap(width: 96, height: 96);
        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 100, Gain: 0, AutoExposure: false, AutoGain: false);
        var encoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);

        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            encoding,
            "image/png",
            "png",
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var fitsOptions = new Mock<IOptionsMonitor<FitsExportOptions>>();
        fitsOptions.SetupGet(o => o.CurrentValue).Returns(new FitsExportOptions { EnableForProcessed = true, EnableForRaw = false });

        var rigAdapter = new Mock<IRigAcquisitionAdapter>(MockBehavior.Strict);
        rigAdapter.SetupGet(r => r.ActiveRig).Returns(RigPresets.MockAsi174_Fujinon);

        var fitsEncoder = new Mock<IFitsFrameEncoder>(MockBehavior.Strict);
        // FITS encoder should not be called for default (UI) context

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder.Object,
            rigAdapter.Object,
            fitsOptions.Object);

        var delivery = encoder.Encode(frame); // Default context should be UserInterface

        Assert.AreEqual("image/png", delivery.ContentType, "Default context should return PNG even when FITS is enabled.");
        Assert.AreEqual("png", delivery.FileExtension, "Default context should use png file extension.");
        Assert.IsGreaterThan(0, delivery.Payload.Length, "Default encoder should emit non-empty payload.");

        // FITS encoder should not have been called for default (UI) context
        fitsEncoder.Verify(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), It.IsAny<FitsExportOptions>()), Times.Never);
    }

    [TestMethod]
    public void Encode_WithCustomEncoding_OverridesFrameSettings()
    {
        // Arrange: Frame has PNG @ 100% but custom encoding specifies JPEG @ 80%
        using var bitmap = new SKBitmap(width: 16, height: 16);
        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 1000, Gain: 200, AutoExposure: false, AutoGain: false);
        var frameEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);

        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            frameEncoding,
            ImageEncodingUtilities.ToContentType(frameEncoding.Format),
            ImageEncodingUtilities.ToFileExtension(frameEncoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var fitsOptions = new Mock<IOptionsMonitor<FitsExportOptions>>();
        fitsOptions.SetupGet(o => o.CurrentValue).Returns(new FitsExportOptions { EnableForProcessed = false, EnableForRaw = false });
        var rigAdapter = new Mock<IRigAcquisitionAdapter>();
        rigAdapter.SetupGet(r => r.ActiveRig).Returns(RigPresets.MockAsi174_Fujinon);
        var fitsEncoder = new Mock<IFitsFrameEncoder>();

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder.Object,
            rigAdapter.Object,
            fitsOptions.Object);

        // Act: Encode with custom JPEG @ 80% instead of frame's PNG @ 100%
        var customEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 80);
        var delivery = encoder.Encode(frame, ProcessedFrameEncodingContext.UserInterface, customEncoding);

        // Assert: Should use custom encoding (JPEG) not frame encoding (PNG)
        Assert.AreEqual("image/jpeg", delivery.ContentType, "Custom encoding should override frame encoding for content type.");
        Assert.AreEqual("jpg", delivery.FileExtension, "Custom encoding should override frame encoding for file extension.");
        Assert.IsGreaterThan(0, delivery.Payload.Length, "Encoder should emit non-empty payload with custom encoding.");
    }

    [TestMethod]
    public void Encode_CustomFits_UsesUnifiedFitsEncoder()
    {
        using var bitmap = new SKBitmap(width: 8, height: 8);
        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 500, Gain: 100, AutoExposure: false, AutoGain: false);
        var frameEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);

        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            frameEncoding,
            ImageEncodingUtilities.ToContentType(frameEncoding.Format),
            ImageEncodingUtilities.ToFileExtension(frameEncoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var fitsOptionsMonitor = Mock.Of<IOptionsMonitor<FitsExportOptions>>(m => m.CurrentValue == new FitsExportOptions());
        var rigAdapter = Mock.Of<IRigAcquisitionAdapter>(a => a.ActiveRig == RigPresets.MockAsi174_Fujinon);

        var expected = new byte[] { 0x01, 0x02, 0x03 };
        var fitsEncoder = new Mock<IFitsFrameEncoder>(MockBehavior.Strict);
        fitsEncoder
            .Setup(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), It.IsAny<FitsEncodingOptions?>()))
            .Returns(new ProcessedFrameDelivery(expected, "application/fits", "fits"));

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            fitsEncoder.Object,
            rigAdapter,
            fitsOptionsMonitor);

        var custom = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
        {
            FitsOptions = new FitsEncodingOptions
            {
                BitDepth = HVO.SkyMonitorV5.RPi.Pipeline.FitsBitDepth.U16,
                ImageFormat = HVO.SkyMonitorV5.RPi.Pipeline.FitsImageFormat.Mono,
                Compression = HVO.SkyMonitorV5.RPi.Pipeline.FitsCompression.None,
                UnsignedU16 = true,
                WriteChecksum = true
            }
        };

        var delivery = encoder.Encode(frame, ProcessedFrameEncodingContext.UserInterface, custom);

        Assert.AreEqual("application/fits", delivery.ContentType);
        Assert.AreEqual("fits", delivery.FileExtension);
        CollectionAssert.AreEqual(expected, delivery.Payload.ToArray());

        fitsEncoder.Verify(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), It.IsAny<FitsEncodingOptions?>()), Times.Once);
    }

    [TestMethod]
    public void Encode_TiffFormat_ThrowsNotSupported()
    {
        using var bitmap = new SKBitmap(width: 4, height: 4);
        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 100, Gain: 0, AutoExposure: false, AutoGain: false);
        var frameEncoding = new ImageEncodingSettings(ImageEncodingFormat.Tiff, 90);

        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            frameEncoding,
            ImageEncodingUtilities.ToContentType(frameEncoding.Format),
            ImageEncodingUtilities.ToFileExtension(frameEncoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            Mock.Of<IFitsFrameEncoder>(),
            Mock.Of<IRigAcquisitionAdapter>(a => a.ActiveRig == RigPresets.MockAsi174_Fujinon),
            Mock.Of<IOptionsMonitor<FitsExportOptions>>(m => m.CurrentValue == new FitsExportOptions()));

        try
        {
            encoder.Encode(frame);
            Assert.Fail("Expected NotSupportedException for TIFF format.");
        }
        catch (NotSupportedException)
        {
            // expected
        }
    }

    [TestMethod]
    public void Encode_XisfFormat_ThrowsNotSupported()
    {
        using var bitmap = new SKBitmap(width: 4, height: 4);
        using var image = SKImage.FromBitmap(bitmap);
        var exposure = new ExposureSettings(ExposureMilliseconds: 100, Gain: 0, AutoExposure: false, AutoGain: false);
        var frameEncoding = new ImageEncodingSettings(ImageEncodingFormat.Xisf, 90);

        var frame = new ProcessedFrame(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            exposure,
            frameEncoding,
            ImageEncodingUtilities.ToContentType(frameEncoding.Format),
            ImageEncodingUtilities.ToFileExtension(frameEncoding.Format),
            FramesStacked: 1,
            IntegrationMilliseconds: exposure.ExposureMilliseconds,
            AppliedFilters: Array.Empty<string>(),
            ProcessingMilliseconds: 0,
            ImmutableImage: image);

        var encoder = new ProcessedFrameEncoder(
            NullLogger<ProcessedFrameEncoder>.Instance,
            Mock.Of<IFitsFrameEncoder>(),
            Mock.Of<IRigAcquisitionAdapter>(a => a.ActiveRig == RigPresets.MockAsi174_Fujinon),
            Mock.Of<IOptionsMonitor<FitsExportOptions>>(m => m.CurrentValue == new FitsExportOptions()));

        try
        {
            encoder.Encode(frame);
            Assert.Fail("Expected NotSupportedException for XISF format.");
        }
        catch (NotSupportedException)
        {
            // expected
        }
    }
}

#pragma warning restore CS0618
