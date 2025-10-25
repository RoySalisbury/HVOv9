using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Skia;

/// <summary>
/// Provides helpers for producing independent raster <see cref="SKImage"/> instances and
/// converting mutable bitmaps into immutable snapshots.
/// </summary>
internal static class SkiaImageUtilities
{
    private static readonly SKColorSpace LinearSrgb = SKColorSpace.CreateSrgbLinear();

    public static SKImage? CloneToRaster(SKImage? source)
    {
        if (source is null)
        {
            return null;
        }

        var colorType = source.ColorType == SKColorType.Unknown ? SKColorType.RgbaF16 : source.ColorType;
        var alphaType = source.AlphaType == SKAlphaType.Unknown ? SKAlphaType.Premul : source.AlphaType;
        var colorSpace = source.ColorSpace ?? LinearSrgb;
        var info = new SKImageInfo(source.Width, source.Height, colorType, alphaType, colorSpace);
        if (info.BytesSize <= 0)
        {
            return null;
        }

        var buffer = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var pointer = handle.AddrOfPinnedObject();
            if (!source.ReadPixels(info, pointer, info.RowBytes))
            {
                return null;
            }

            return SKImage.FromPixelCopy(info, pointer, info.RowBytes);
        }
        finally
        {
            handle.Free();
        }
    }

    public static SKImage? SnapshotToImmutable(SKImage? sourceImage, SKBitmap? fallbackBitmap)
    {
        if (sourceImage is not null)
        {
            return CloneToRaster(sourceImage);
        }

        if (fallbackBitmap is null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(fallbackBitmap);
        return CloneToRaster(image);
    }

    public static SKBitmap CreateBitmapCopy(SKImage image, SKColorType colorType = SKColorType.Bgra8888, SKAlphaType alphaType = SKAlphaType.Premul)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        var info = new SKImageInfo(image.Width, image.Height, colorType, alphaType);
        var bitmap = new SKBitmap(info);
        var pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Failed to allocate pixel memory for bitmap copy.");
        }

        if (!image.ReadPixels(info, pixels, bitmap.RowBytes))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Failed to read pixels from image into bitmap copy.");
        }

        return bitmap;
    }
}
