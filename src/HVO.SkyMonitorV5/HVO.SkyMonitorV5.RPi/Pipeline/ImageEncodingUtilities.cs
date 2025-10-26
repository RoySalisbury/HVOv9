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
        _ => SKEncodedImageFormat.Png
    };

    public static string ToContentType(ImageEncodingFormat format) => format switch
    {
        ImageEncodingFormat.Jpeg => "image/jpeg",
        ImageEncodingFormat.Png => "image/png",
        _ => "application/octet-stream"
    };

    public static string? ToFileExtension(ImageEncodingFormat format) => format switch
    {
        ImageEncodingFormat.Jpeg => "jpg",
        ImageEncodingFormat.Png => "png",
        _ => null
    };

    public static ImageEncodingSettings Normalize(ImageEncodingSettings? settings)
        => settings is null
            ? new ImageEncodingSettings()
            : settings with { Quality = Math.Clamp(settings.Quality, 1, 100) };
}
