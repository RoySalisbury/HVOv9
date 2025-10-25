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

        var delivery = encoder.Encode(frame);

        Assert.AreEqual("application/fits", delivery.ContentType, "FITS-enabled encoding should advertise application/fits content type.");
        Assert.AreEqual("fits", delivery.FileExtension, "FITS-enabled delivery should use fits file extension.");
        Assert.IsGreaterThan(0, delivery.Payload.Length, "FITS-enabled encoder should emit non-empty payload.");

        fitsEncoder.Verify(e => e.EncodeProcessed(It.IsAny<ProcessedFrame>(), It.IsAny<RigSpec>(), It.IsAny<FitsExportOptions>()), Times.Once);
    }
}
