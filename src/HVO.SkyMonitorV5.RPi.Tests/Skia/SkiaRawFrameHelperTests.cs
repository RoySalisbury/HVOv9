using HVO.SkyMonitorV5.RPi.Skia;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.Skia;

[TestClass]
public sealed class SkiaRawFrameHelperTests
{
    [TestMethod]
    public void TryCreateRawPayload_ProducesDescriptorAndPayload()
    {
        var info = new SKImageInfo(8, 4, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(new SKColorF(0.1f, 0.2f, 0.3f, 1f));
        using var image = surface.Snapshot();

        var success = SkiaRawFrameHelper.TryCreateRawPayload(image, out var payload, out var descriptor);

        Assert.IsTrue(success, "Raw payload creation should succeed for raster images.");
        Assert.IsNotNull(payload, "Payload should be materialized when helper succeeds.");
        Assert.IsNotNull(descriptor, "Descriptor should accompany the raw payload.");
        Assert.AreEqual(info.Width, descriptor.Width, "Descriptor width should match source image.");
        Assert.AreEqual(info.Height, descriptor.Height, "Descriptor height should match source image.");
        Assert.AreEqual(descriptor.RowBytes * descriptor.Height, payload.Length, "Payload length should match row bytes.");
        Assert.IsTrue(descriptor.GammaIsLinear, "Linear color space should be preserved in descriptor.");
    }

    [TestMethod]
    public void TryCreateDescriptor_FromBitmapReturnsMetadata()
    {
        var info = new SKImageInfo(4, 2, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var bitmap = new SKBitmap(info);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.SkyBlue);

        var descriptor = SkiaRawFrameHelper.TryCreateDescriptor(bitmap);

        Assert.IsNotNull(descriptor, "Bitmap descriptor generation should succeed.");
        Assert.AreEqual(info.Width, descriptor!.Width, "Descriptor width should match bitmap.");
        Assert.AreEqual(info.Height, descriptor.Height, "Descriptor height should match bitmap.");
        Assert.AreEqual(info.BytesPerPixel, descriptor.BytesPerPixel, "Bytes-per-pixel should map to bitmap info.");
    }
}
