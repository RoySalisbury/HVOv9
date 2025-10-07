using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents an uncompressed pixel buffer associated with a captured frame.
/// </summary>
public sealed record FramePixelBuffer(int Width, int Height, int RowBytes, int BytesPerPixel, byte[] PixelBytes)
{
    public static FramePixelBuffer FromBitmap(SKBitmap bitmap)
    {
        var span = bitmap.GetPixelSpan();
        var buffer = new byte[span.Length];
        span.CopyTo(buffer);
        return new FramePixelBuffer(bitmap.Width, bitmap.Height, bitmap.Info.RowBytes, bitmap.Info.BytesPerPixel, buffer);
    }
}
