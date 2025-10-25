#if HAS_SKIA
using System;
using System.IO;
using HVO.Astronomy.CFITSIO;
using HVO.Astronomy.CFITSIO.Interop;
using SkiaSharp;

namespace HVO.Astronomy.CFITSIO
{
  /// <summary>
  /// In-memory FITS helpers for SkiaSharp.
  /// Produce/consume FITS byte[] without writing to disk.
  /// </summary>
  public static class SkiaFitsInMemoryExtensions
  {
    /// <summary>
    /// Convert an <see cref="SKBitmap"/> to a FITS (U16 grayscale) byte array in memory.
    /// </summary>
    /// <param name="bitmap">Source bitmap (any color type). Will be converted to Gray8, then expanded to U16.</param>
    /// <param name="compressionPolicy">
    /// Optional per-HDU compression settings. If supplied, applied to the current HDU (tiled compression).
    /// If you just want “whole-file defaults” (like fpack), call <see cref="RecompressFitsBytes"/> after this.
    /// </param>
    /// <param name="stampHeader">Optional callback to write keywords (e.g., WCS) on the current HDU.</param>
    public static byte[] ToFitsU16Bytes(this SKBitmap bitmap,
                                        FitsCompressionPolicy? compressionPolicy = null,
                                        Action<FitsFile>? stampHeader = null)
    {
      if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));

      // Extract Gray8 → expand to U16 plane
      var (w, h, planeU16) = ExtractGrayU16(bitmap);

      // Create an in-memory FITS, write the image, stamp headers, (optionally) apply compression policy
      using var mf = FitsFile.CreateInMemory();
      FitsImage.WriteU16(mf, w, h, planeU16);

      stampHeader?.Invoke(mf);
      if (compressionPolicy != null)
        mf.ApplyCompressionPolicyToCurrentHdu(compressionPolicy);

      return mf.ToArray();
    }

    /// <summary>
    /// Convert an <see cref="SKImage"/> to a FITS (U16 grayscale) byte array in memory.
    /// </summary>
    public static byte[] ToFitsU16Bytes(this SKImage image,
                                        FitsCompressionPolicy? compressionPolicy = null,
                                        Action<FitsFile>? stampHeader = null)
    {
      if (image == null) throw new ArgumentNullException(nameof(image));
      using var bmp = SKBitmap.FromImage(image);
      return bmp.ToFitsU16Bytes(compressionPolicy, stampHeader);
    }

    /// <summary>
    /// Write a FITS (U16 grayscale) representation of this <see cref="SKBitmap"/> directly to a stream.
    /// </summary>
    public static void SaveAsFitsU16(this SKBitmap bitmap,
                                     Stream output,
                                     FitsCompressionPolicy? compressionPolicy = null,
                                     Action<FitsFile>? stampHeader = null)
    {
      if (output == null) throw new ArgumentNullException(nameof(output));
      var bytes = bitmap.ToFitsU16Bytes(compressionPolicy, stampHeader);
      output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Load a FITS image (from memory) into a new Gray8 <see cref="SKBitmap"/>.
    /// </summary>
    /// <param name="fitsBytes">FITS byte buffer.</param>
    /// <param name="preferU16">
    /// If true, reads as U16 then down-converts to Gray8 (keep dynamic range); else read as U8 if present.
    /// </param>

    // TODO: Re-implement after memfile testing
    // /// <summary>
    // /// Recompress FITS in memory using CFITSIO's image-compress (all image HDUs).
    // /// </summary>
    // public static byte[] RecompressFitsBytes(this byte[] fitsBytes)
    // {
    //   if (fitsBytes == null || fitsBytes.Length == 0) throw new ArgumentException("Empty FITS buffer.", nameof(fitsBytes));
    //
    //   using var src = FitsFile.OpenFromMemory(fitsBytes, readWrite: false);
    //   return src.ToArray(); // Placeholder - need to implement compress-to-memory flow
    // }

    // ────────────────────────── helpers ──────────────────────────

    /// <summary>
    /// Extract a 16-bit grayscale plane from a bitmap.
    /// If Gray8, expands by byte replication. Otherwise convert via ReadPixels → Gray8 → expand.
    /// </summary>
    private static (int Width, int Height, ushort[] Plane) ExtractGrayU16(SKBitmap bitmap)
    {
      var info = bitmap.Info;
      int width = info.Width;
      int height = info.Height;

      if (info.ColorType == SKColorType.Gray8)
      {
        var src = bitmap.GetPixelSpan();
        var u16 = new ushort[checked(width * height)];
        for (int i = 0; i < src.Length; i++)
        {
          byte v = src[i];
          u16[i] = (ushort)(v << 8 | v);
        }
        return (width, height, u16);
      }

      // Convert to Gray8 using ReadPixels
      var grayInfo = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
      using var grayBmp = new SKBitmap();
      if (!grayBmp.TryAllocPixels(grayInfo))
        throw new InvalidOperationException("Failed to allocate Gray8 bitmap.");

      using (var img = SKImage.FromBitmap(bitmap))
      using (var pix = grayBmp.PeekPixels() ?? throw new InvalidOperationException("Unable to access pixel data."))
      {
        bool ok = img.ReadPixels(grayInfo, pix.GetPixels(), pix.RowBytes, 0, 0);
        if (!ok) throw new InvalidOperationException("Skia failed to convert to Gray8.");
      }

      // Expand Gray8 → U16
      var src8 = grayBmp.GetPixelSpan();
      var outU16 = new ushort[checked(width * height)];
      for (int i = 0; i < src8.Length; i++)
      {
        byte v = src8[i];
        outU16[i] = (ushort)(v << 8 | v);
      }

      return (width, height, outU16);
    }
  }
}
#endif
