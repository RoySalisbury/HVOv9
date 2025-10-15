using System;
using System.Runtime.InteropServices;
using HVO.SkyMonitorV5.RPi.Skia;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Tests.TestHelpers;

internal static class SkiaTestImageFactory
{
    public static SKImage CreateLinearGradientImage(int width, int height, float redScale = 1f, float greenScale = 1f, float blueValue = 0.5f)
        => CreateLinearGradientImage(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, null, redScale, greenScale, blueValue);

    public static SKImage CreateLinearGradientImage(int width, int height, SKColorType colorType, SKAlphaType alphaType, SKColorSpace? colorSpace, float redScale, float greenScale, float blueValue)
    {
        var info = new SKImageInfo(width, height, colorType, alphaType, colorSpace);
        using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException($"Failed to allocate surface {width}x{height}.");
        WriteGradient(surface, redScale, greenScale, blueValue);

        var snapshot = surface.Snapshot() ?? throw new InvalidOperationException("Failed to snapshot gradient surface.");
        try
        {
            return SkiaImageUtilities.CloneToRaster(snapshot)
                ?? throw new InvalidOperationException("Failed to clone gradient image.");
        }
        finally
        {
            snapshot.Dispose();
        }
    }

    public static SKBitmap CreateLinearGradientBitmap(int width, int height, float redScale = 1f, float greenScale = 1f, float blueValue = 0.5f)
        => CreateLinearGradientBitmap(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, null, redScale, greenScale, blueValue);

    public static SKBitmap CreateLinearGradientBitmap(int width, int height, SKColorType colorType, SKAlphaType alphaType, SKColorSpace? colorSpace, float redScale, float greenScale, float blueValue)
    {
        var info = new SKImageInfo(width, height, colorType, alphaType, colorSpace);
        var bitmap = new SKBitmap(info);
        var pixmap = bitmap.PeekPixels();
        if (pixmap is null)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Failed to access bitmap pixels.");
        }

        try
        {
            WriteGradient(pixmap, redScale, greenScale, blueValue);
        }
        finally
        {
            pixmap.Dispose();
        }

        return bitmap;
    }

    public static SKImage CreateMonochromeGradientImage(int width, int height, float minValue = 0.05f, float maxValue = 0.95f)
        => CreateMonochromeGradientImage(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, null, minValue, maxValue);

    public static SKImage CreateMonochromeGradientImage(int width, int height, SKColorType colorType, SKAlphaType alphaType, SKColorSpace? colorSpace, float minValue, float maxValue)
    {
        var info = new SKImageInfo(width, height, colorType, alphaType, colorSpace);
        using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException($"Failed to allocate surface {width}x{height}.");
        WriteMonochromeGradient(surface, minValue, maxValue);

        var snapshot = surface.Snapshot() ?? throw new InvalidOperationException("Failed to snapshot monochrome gradient surface.");
        try
        {
            return SkiaImageUtilities.CloneToRaster(snapshot)
                ?? throw new InvalidOperationException("Failed to clone monochrome gradient image.");
        }
        finally
        {
            snapshot.Dispose();
        }
    }

    public static SKBitmap CreateMonochromeGradientBitmap(int width, int height, float minValue = 0.05f, float maxValue = 0.95f)
        => CreateMonochromeGradientBitmap(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, null, minValue, maxValue);

    public static SKBitmap CreateMonochromeGradientBitmap(int width, int height, SKColorType colorType, SKAlphaType alphaType, SKColorSpace? colorSpace, float minValue, float maxValue)
    {
        var info = new SKImageInfo(width, height, colorType, alphaType, colorSpace);
        var bitmap = new SKBitmap(info);
        var pixmap = bitmap.PeekPixels();
        if (pixmap is null)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Failed to access bitmap pixels.");
        }

        try
        {
            WriteMonochromeGradient(pixmap, minValue, maxValue);
        }
        finally
        {
            pixmap.Dispose();
        }

        return bitmap;
    }

    public static Half[] GetHalfPixelBuffer(SKImage image)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.RgbaF16, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes))
        {
            throw new InvalidOperationException("Failed to read pixels into buffer.");
        }

        var span = bitmap.GetPixelSpan();
        var halfSpan = MemoryMarshal.Cast<byte, Half>(span);
        var buffer = new Half[halfSpan.Length];
        halfSpan.CopyTo(buffer);
        return buffer;
    }

    public static float[] GetFloatPixelBuffer(SKImage image)
    {
        var halfData = GetHalfPixelBuffer(image);
        return ConvertHalfSpanToFloat(halfData);
    }

    public static byte[] GetBitmapPixelBuffer(SKBitmap bitmap)
    {
        var span = bitmap.GetPixelSpan();
        var buffer = new byte[span.Length];
        span.CopyTo(buffer);
        return buffer;
    }

    public static byte[] GetBytePixelBuffer(SKImage image, SKColorType colorType)
    {
        var info = new SKImageInfo(image.Width, image.Height, colorType, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        if (!image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes))
        {
            throw new InvalidOperationException("Failed to read pixels into buffer.");
        }

        return GetBitmapPixelBuffer(bitmap);
    }

    public static float[] GetNormalizedFloatPixelBuffer(SKImage image, SKColorType colorType)
    {
        if (colorType == SKColorType.RgbaF16)
        {
            return GetFloatPixelBuffer(image);
        }

        if (colorType is SKColorType.Rgba8888 or SKColorType.Bgra8888)
        {
            var bytes = GetBytePixelBuffer(image, colorType);
            return ConvertByteSpanToFloat(bytes);
        }

        throw new NotSupportedException($"Unsupported color type {colorType} for normalized float buffer extraction.");
    }

    public static float[] GetNormalizedFloatPixelBuffer(SKBitmap bitmap)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        var span = bitmap.GetPixelSpan();

        return bitmap.Info.ColorType switch
        {
            SKColorType.RgbaF16 => ConvertHalfSpanToFloat(MemoryMarshal.Cast<byte, Half>(span)),
            SKColorType.Rgba8888 or SKColorType.Bgra8888 => ConvertByteSpanToFloat(span),
            _ => throw new NotSupportedException($"Unsupported color type {bitmap.Info.ColorType} for normalized float buffer extraction."),
        };
    }

    private static void WriteGradient(SKSurface surface, float redScale, float greenScale, float blueValue)
    {
        var pixmap = surface.PeekPixels();
        if (pixmap is null)
        {
            throw new InvalidOperationException("Failed to access surface pixels.");
        }

        try
        {
            WriteGradient(pixmap, redScale, greenScale, blueValue);
        }
        finally
        {
            pixmap.Dispose();
        }
    }

    private static void WriteMonochromeGradient(SKSurface surface, float minValue, float maxValue)
    {
        var pixmap = surface.PeekPixels();
        if (pixmap is null)
        {
            throw new InvalidOperationException("Failed to access surface pixels.");
        }

        try
        {
            WriteMonochromeGradient(pixmap, minValue, maxValue);
        }
        finally
        {
            pixmap.Dispose();
        }
    }

    private static void WriteGradient(SKPixmap pixmap, float redScale, float greenScale, float blueValue)
    {
        var width = pixmap.Width;
        var height = pixmap.Height;
        var span = pixmap.GetPixelSpan();

        var denominatorX = Math.Max(1, width - 1);
        var denominatorY = Math.Max(1, height - 1);

        switch (pixmap.ColorType)
        {
            case SKColorType.RgbaF16:
                WriteGradientF16(span, width, height, denominatorX, denominatorY, redScale, greenScale, blueValue);
                break;
            case SKColorType.Rgba8888:
                WriteGradient8888(span, width, height, denominatorX, denominatorY, redScale, greenScale, blueValue, isBgra: false);
                break;
            case SKColorType.Bgra8888:
                WriteGradient8888(span, width, height, denominatorX, denominatorY, redScale, greenScale, blueValue, isBgra: true);
                break;
            default:
                throw new NotSupportedException($"Unsupported color type {pixmap.ColorType} for gradient generation.");
        }
    }

    private static void WriteMonochromeGradient(SKPixmap pixmap, float minValue, float maxValue)
    {
        var width = pixmap.Width;
        var height = pixmap.Height;
        var span = pixmap.GetPixelSpan();
        var denominatorX = Math.Max(1, width - 1);
        var denominatorY = Math.Max(1, height - 1);

        switch (pixmap.ColorType)
        {
            case SKColorType.RgbaF16:
                WriteMonochromeGradientF16(span, width, height, denominatorX, denominatorY, minValue, maxValue);
                break;
            case SKColorType.Rgba8888:
                WriteMonochromeGradient8888(span, width, height, denominatorX, denominatorY, minValue, maxValue, isBgra: false);
                break;
            case SKColorType.Bgra8888:
                WriteMonochromeGradient8888(span, width, height, denominatorX, denominatorY, minValue, maxValue, isBgra: true);
                break;
            default:
                throw new NotSupportedException($"Unsupported color type {pixmap.ColorType} for gradient generation.");
        }
    }

    private static void WriteGradientF16(Span<byte> span, int width, int height, int denominatorX, int denominatorY, float redScale, float greenScale, float blueValue)
    {
        var halfSpan = MemoryMarshal.Cast<byte, Half>(span);
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            var g = (float)y / denominatorY * greenScale;
            for (var x = 0; x < width; x++)
            {
                var r = (float)x / denominatorX * redScale;
                halfSpan[index++] = (Half)r;
                halfSpan[index++] = (Half)g;
                halfSpan[index++] = (Half)blueValue;
                halfSpan[index++] = Half.One;
            }
        }
    }

    private static void WriteGradient8888(Span<byte> span, int width, int height, int denominatorX, int denominatorY, float redScale, float greenScale, float blueValue, bool isBgra)
    {
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            var g = (float)y / denominatorY * greenScale;
            var gByte = ToByte(g);
            for (var x = 0; x < width; x++)
            {
                var r = (float)x / denominatorX * redScale;
                var rByte = ToByte(r);
                var bByte = ToByte(blueValue);

                if (isBgra)
                {
                    span[index++] = bByte;
                    span[index++] = gByte;
                    span[index++] = rByte;
                    span[index++] = 255;
                }
                else
                {
                    span[index++] = rByte;
                    span[index++] = gByte;
                    span[index++] = bByte;
                    span[index++] = 255;
                }
            }
        }
    }

    private static void WriteMonochromeGradientF16(Span<byte> span, int width, int height, int denominatorX, int denominatorY, float minValue, float maxValue)
    {
        var halfSpan = MemoryMarshal.Cast<byte, Half>(span);
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            var fractionY = (float)y / denominatorY;
            for (var x = 0; x < width; x++)
            {
                var fractionX = (float)x / denominatorX;
                var value = minValue + ((fractionX + fractionY) * 0.5f) * (maxValue - minValue);
                var halfValue = (Half)value;
                halfSpan[index++] = halfValue;
                halfSpan[index++] = halfValue;
                halfSpan[index++] = halfValue;
                halfSpan[index++] = Half.One;
            }
        }
    }

    private static void WriteMonochromeGradient8888(Span<byte> span, int width, int height, int denominatorX, int denominatorY, float minValue, float maxValue, bool isBgra)
    {
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            var fractionY = (float)y / denominatorY;
            for (var x = 0; x < width; x++)
            {
                var fractionX = (float)x / denominatorX;
                var value = minValue + ((fractionX + fractionY) * 0.5f) * (maxValue - minValue);
                var byteValue = ToByte(value);

                if (isBgra)
                {
                    span[index++] = byteValue;
                    span[index++] = byteValue;
                    span[index++] = byteValue;
                    span[index++] = 255;
                }
                else
                {
                    span[index++] = byteValue;
                    span[index++] = byteValue;
                    span[index++] = byteValue;
                    span[index++] = 255;
                }
            }
        }
    }

    public static float ConvertSrgbToLinear(float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);
        if (clamped <= 0.04045f)
        {
            return clamped / 12.92f;
        }

        return MathF.Pow((clamped + 0.055f) / 1.055f, 2.4f);
    }

    private static byte ToByte(float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);
        return (byte)MathF.Round(clamped * 255f, MidpointRounding.AwayFromZero);
    }

    private static float[] ConvertHalfSpanToFloat(ReadOnlySpan<Half> halfSpan)
    {
        var result = new float[halfSpan.Length];
        for (var i = 0; i < halfSpan.Length; i++)
        {
            result[i] = (float)halfSpan[i];
        }

        return result;
    }

    private static float[] ConvertByteSpanToFloat(ReadOnlySpan<byte> byteSpan)
    {
        var result = new float[byteSpan.Length];
        for (var i = 0; i < byteSpan.Length; i++)
        {
            result[i] = byteSpan[i] / 255f;
        }

        return result;
    }
}
