#nullable enable

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Cameras.Zwo;

/// <summary>
/// Span-oriented helpers for converting ASICamera2 capture formats into Skia bitmaps.
/// </summary>
internal static class ZwoPixelConverter
{
    public static SKBitmap CreateBgraBitmapFromRgb24(IntPtr sourceBuffer, int width, int height, int captureRowBytes)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var destination = bitmap.GetPixelSpan();

        var bufferLength = captureRowBytes * height;
        var rental = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            Marshal.Copy(sourceBuffer, rental, 0, bufferLength);
            var source = rental.AsSpan(0, bufferLength);

            for (var row = 0; row < height; row++)
            {
                var sourceRow = source.Slice(row * captureRowBytes, captureRowBytes);
                var destinationRow = destination.Slice(row * width * 4, width * 4);
                ConvertRgbRowToBgra(sourceRow, destinationRow, width);
            }

            return bitmap;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rental, clearArray: false);
        }
    }

    public static SKBitmap CreateGrayBitmapFromRaw16(IntPtr sourceBuffer, int width, int height, int captureRowBytes)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        var destination = bitmap.GetPixelSpan();

        var bufferLength = captureRowBytes * height;
        var rental = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            Marshal.Copy(sourceBuffer, rental, 0, bufferLength);
            var source = rental.AsSpan(0, bufferLength);

            for (var row = 0; row < height; row++)
            {
                var sourceRow = source.Slice(row * captureRowBytes, captureRowBytes);
                var destinationRow = destination.Slice(row * width, width);
                ConvertRaw16RowToGray(sourceRow, destinationRow, width);
            }

            return bitmap;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rental, clearArray: false);
        }
    }

    public static SKBitmap CreateGrayBitmapFromY8(IntPtr sourceBuffer, int width, int height, int captureRowBytes)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        var destination = bitmap.GetPixelSpan();

        var bufferLength = captureRowBytes * height;
        var rental = ArrayPool<byte>.Shared.Rent(bufferLength);
        try
        {
            Marshal.Copy(sourceBuffer, rental, 0, bufferLength);
            var source = rental.AsSpan(0, bufferLength);

            for (var row = 0; row < height; row++)
            {
                var copyLength = Math.Min(width, captureRowBytes);
                var sourceRow = source.Slice(row * captureRowBytes, captureRowBytes);
                var destinationRow = destination.Slice(row * width, copyLength);
                sourceRow.Slice(0, copyLength).CopyTo(destinationRow);
            }

            return bitmap;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rental, clearArray: false);
        }
    }

    private static void ConvertRgbRowToBgra(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        var sourceIndex = 0;
        var destinationIndex = 0;

        for (var column = 0; column < width; column++)
        {
            if (sourceIndex + 2 >= source.Length || destinationIndex + 3 >= destination.Length)
            {
                break;
            }

            var r = source[sourceIndex++];
            var g = source[sourceIndex++];
            var b = source[sourceIndex++];

            destination[destinationIndex++] = b;
            destination[destinationIndex++] = g;
            destination[destinationIndex++] = r;
            destination[destinationIndex++] = 255;
        }

        // TODO(dotnet10): Replace scalar channel expansion with SIMD once wider intrinsics support 24-bit to 32-bit promotion.
    }

    private static void ConvertRaw16RowToGray(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        var sourceIndex = 0;

        for (var column = 0; column < width; column++)
        {
            if (sourceIndex + 1 >= source.Length || column >= destination.Length)
            {
                break;
            }

            var low = source[sourceIndex++];
            var high = source[sourceIndex++];
            var value = (ushort)(low | (high << 8));
            destination[column] = (byte)(value >> 8);
        }

        // TODO(dotnet10): Introduce vectorized ushort->byte narrowing path using hardware intrinsics when available.
    }
}
