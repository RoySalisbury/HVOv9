using System;
using HVO.SkyMonitorV5.RPi.Exports;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Skia;

/// <summary>
/// Provides helpers for extracting raw pixel data and descriptors from Skia images.
/// </summary>
internal static class SkiaRawFrameHelper
{
    public const string RawContentType = "application/vnd.hvo.skia.raw";
    public const string RawFileExtension = "skimg";

    public static bool TryCreateRawPayload(SKImage image, out byte[] payload, out FrameExportImageDescriptor descriptor)
    {
        if (image is null)
        {
            payload = Array.Empty<byte>();
            descriptor = null!;
            return false;
        }

        using var pixmap = image.PeekPixels();
        if (pixmap is not null)
        {
            return TryCreateRawPayloadFromPixmap(pixmap, out payload, out descriptor);
        }

        using var rasterImage = image.ToRasterImage();
        if (rasterImage is null)
        {
            payload = Array.Empty<byte>();
            descriptor = null!;
            return false;
        }

        using var rasterPixmap = rasterImage.PeekPixels();
        if (rasterPixmap is null)
        {
            payload = Array.Empty<byte>();
            descriptor = null!;
            return false;
        }

        return TryCreateRawPayloadFromPixmap(rasterPixmap, out payload, out descriptor);
    }

    public static FrameExportImageDescriptor? TryCreateDescriptor(SKImage image)
    {
        if (image is null)
        {
            return null;
        }

        using var pixmap = image.PeekPixels();
        if (pixmap is not null)
        {
            return CreateDescriptor(pixmap);
        }

        using var rasterImage = image.ToRasterImage();
        if (rasterImage is null)
        {
            return null;
        }

        using var rasterPixmap = rasterImage.PeekPixels();
        return rasterPixmap is null ? null : CreateDescriptor(rasterPixmap);
    }

    public static FrameExportImageDescriptor? TryCreateDescriptor(SKBitmap bitmap)
    {
        if (bitmap is null)
        {
            return null;
        }

        using var pixmap = bitmap.PeekPixels();
        return pixmap is null ? null : CreateDescriptor(pixmap);
    }

    private static bool TryCreateRawPayloadFromPixmap(SKPixmap pixmap, out byte[] payload, out FrameExportImageDescriptor descriptor)
    {
        var pixelSpan = pixmap.GetPixelSpan();
        if (pixelSpan.Length == 0)
        {
            payload = Array.Empty<byte>();
            descriptor = null!;
            return false;
        }

        payload = pixelSpan.ToArray();
        descriptor = CreateDescriptor(pixmap);
        return true;
    }

    private static FrameExportImageDescriptor CreateDescriptor(SKPixmap pixmap)
    {
        var colorSpace = pixmap.ColorSpace;
        return new FrameExportImageDescriptor(
            pixmap.Width,
            pixmap.Height,
            pixmap.RowBytes,
            pixmap.BytesPerPixel,
            pixmap.ColorType.ToString(),
            pixmap.AlphaType.ToString(),
            colorSpace?.GammaIsLinear ?? false,
            colorSpace?.IsSrgb ?? false,
            colorSpace?.IsNumericalTransferFunction ?? false,
            colorSpace?.ToString());
    }
}
