using System;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Provides helper methods for working with <see cref="ImageEncodingSettings"/> and related formats.
/// </summary>
internal static class ImageEncodingUtilities
{
    public static SKEncodedImageFormat ToSkiaFormat(ImageEncodingFormat format) => format switch
    {
        ImageEncodingFormat.Jpeg => SKEncodedImageFormat.Jpeg,
        ImageEncodingFormat.Png => SKEncodedImageFormat.Png,
        ImageEncodingFormat.Fits => throw new NotSupportedException("FITS format requires specialized encoder"),
        ImageEncodingFormat.Tiff => throw new NotSupportedException("TIFF format not yet implemented"),
        ImageEncodingFormat.Xisf => throw new NotSupportedException("XISF format not yet implemented"),
        _ => SKEncodedImageFormat.Png
    };

    public static string ToContentType(ImageEncodingFormat format) => format switch
    {
        ImageEncodingFormat.Jpeg => "image/jpeg",
        ImageEncodingFormat.Png => "image/png",
        ImageEncodingFormat.Fits => "image/fits",
        ImageEncodingFormat.Tiff => "image/tiff",
        ImageEncodingFormat.Xisf => "application/octet-stream",
        _ => "application/octet-stream"
    };

    public static string? ToFileExtension(ImageEncodingFormat format) => format switch
    {
        ImageEncodingFormat.Jpeg => "jpg",
        ImageEncodingFormat.Png => "png",
        ImageEncodingFormat.Fits => "fits",
        ImageEncodingFormat.Tiff => "tiff",
        ImageEncodingFormat.Xisf => "xisf",
        _ => null
    };

    public static ImageEncodingSettings Normalize(ImageEncodingSettings? settings)
        => settings is null
            ? new ImageEncodingSettings()
            : settings with { Quality = Math.Clamp(settings.Quality, 1, 100) };

    /// <summary>
    /// Determines if the format requires specialized encoding (not Skia-based).
    /// </summary>
    public static bool RequiresSpecializedEncoder(ImageEncodingFormat format) => format switch
    {
        ImageEncodingFormat.Fits => true,
        ImageEncodingFormat.Tiff => true,
        ImageEncodingFormat.Xisf => true,
        _ => false
    };

    /// <summary>
    /// Determines if the format is a raster format suitable for UI display.
    /// </summary>
    public static bool IsRasterFormat(ImageEncodingFormat format) => format switch
    {
        ImageEncodingFormat.Jpeg => true,
        ImageEncodingFormat.Png => true,
        ImageEncodingFormat.Tiff => true,
        _ => false
    };
}
