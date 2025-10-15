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

    [TestMethod]
    public void CloneToRaster_ReturnsIndependentImageAfterSourceDisposed()
    {
        var info = new SKImageInfo(8, 8, SKColorType.RgbaF16, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info) ?? throw new AssertFailedException("Failed to allocate test surface.");
        surface.Canvas.Clear(new SKColorF(0.25f, 0.5f, 0.75f, 1f));

        using var snapshot = surface.Snapshot() ?? throw new AssertFailedException("Failed to snapshot surface.");
        var clone = SkiaImageUtilities.CloneToRaster(snapshot);

        Assert.IsNotNull(clone, "Clone should succeed for a simple snapshot.");

        snapshot.Dispose();

        Assert.AreNotEqual(IntPtr.Zero, clone!.Handle, "Cloned image should expose a valid native handle after source disposal.");
        Assert.AreEqual(info.Width, clone.Width, "Clone should retain the original width.");
        Assert.AreEqual(info.Height, clone.Height, "Clone should retain the original height.");

        using var pixmap = clone.PeekPixels();
        Assert.IsNotNull(pixmap, "Cloned image should provide raster pixels.");

        clone.Dispose();
    }

    [TestMethod]
    public void CloneToRaster_FromPreprocessingSurfaceProducesStableImage()
    {
        var sourceInfo = new SKImageInfo(8, 6, SKColorType.RgbaF16, SKAlphaType.Premul);
        using var sourceSurface = SKSurface.Create(sourceInfo) ?? throw new AssertFailedException("Failed to create source surface.");
        sourceSurface.Canvas.Clear(new SKColorF(0.3f, 0.4f, 0.5f, 1f));

        using var sourceSnapshot = sourceSurface.Snapshot() ?? throw new AssertFailedException("Failed to snapshot source surface.");
        var immutable = SkiaImageUtilities.CloneToRaster(sourceSnapshot) ?? throw new AssertFailedException("Clone from source surface failed.");

        using var preprocessingSurface = SKSurface.Create(sourceInfo) ?? throw new AssertFailedException("Failed to create preprocessing surface.");
        preprocessingSurface.Canvas.Clear(SKColors.Transparent);
        preprocessingSurface.Canvas.DrawImage(immutable, 0, 0);
        preprocessingSurface.Canvas.Flush();

        using var processedSnapshot = preprocessingSurface.Snapshot() ?? throw new AssertFailedException("Failed to snapshot preprocessing surface.");
        var processedClone = SkiaImageUtilities.CloneToRaster(processedSnapshot);

        Assert.IsNotNull(processedClone, "Processed clone should be produced.");

        processedSnapshot.Dispose();
        immutable.Dispose();

        Assert.AreEqual(sourceInfo.Width, processedClone!.Width, "Processed clone should retain width after source disposal.");
        Assert.AreEqual(sourceInfo.Height, processedClone.Height, "Processed clone should retain height after source disposal.");

        using var processedPixmap = processedClone.PeekPixels();
        Assert.IsNotNull(processedPixmap, "Processed clone should expose pixels.");

        processedClone.Dispose();
    }
}
