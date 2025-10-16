using System;
using System.Security.Cryptography;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

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

        var encoder = new ProcessedFrameEncoder(NullLogger<ProcessedFrameEncoder>.Instance);

        var delivery = encoder.Encode(frame);

        Assert.AreEqual("image/png", delivery.ContentType, "PNG encoding should advertise image/png content type.");
        Assert.AreEqual("png", delivery.FileExtension, "PNG delivery should use png file extension.");
        Assert.IsTrue(delivery.Payload.Length > 0, "Encoder should emit non-empty payload.");
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

        var encoder = new ProcessedFrameEncoder(NullLogger<ProcessedFrameEncoder>.Instance);

        var delivery = encoder.Encode(frame);

    var hash = SHA256.HashData(delivery.Payload.Span);
        var hashHex = Convert.ToHexString(hash);

    const string expectedHash = "C22A5C3A47ACA7F5A8E2A146E34ED5DDF5D3F68AAB129054A6758C36C225D196";
    Assert.AreEqual(expectedHash, hashHex, "Deterministic PNG encoding should produce the expected hash.");
    }
}
