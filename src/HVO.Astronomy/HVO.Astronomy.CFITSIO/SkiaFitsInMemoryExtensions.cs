#if HAS_SKIA
using System;
using HVO.Core.Results;
using System.IO;
using HVO; // Result<T>
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
      var r = ToFitsU16BytesResult(bitmap, compressionPolicy, stampHeader);
      if (r.IsFailure) throw r.Error!;
      return r.Value;
    }

    /// <summary>
    /// Result-based variant of <see cref="ToFitsU16Bytes(SKBitmap, FitsCompressionPolicy?, Action{FitsFile}?)"/>.
    /// </summary>
    public static Result<byte[]> ToFitsU16BytesResult(this SKBitmap bitmap,
                                                      FitsCompressionPolicy? compressionPolicy = null,
                                                      Action<FitsFile>? stampHeader = null)
    {
      if (bitmap is null) return Result<byte[]>.Failure(new ArgumentNullException(nameof(bitmap)));

      // Extract Gray8 → expand to U16 plane
      var (w, h, planeU16) = ExtractGrayU16(bitmap);

      try
      {
        using var mf = FitsFile.CreateInMemory();

        // Create U16 image in the primary HDU and write pixels using unsigned scaling
        var rCreate = mf.CreateImageHdu(CFitsIO.USHORT_IMG, w, h);
        if (rCreate.IsFailure) return Result<byte[]>.Failure(rCreate.Error!);
        var rScale = mf.SetScale(1.0, 32768.0);
        if (rScale.IsFailure) return Result<byte[]>.Failure(rScale.Error!);
        var rWrite = mf.WritePixelsU16(1, planeU16);
        if (rWrite.IsFailure) return Result<byte[]>.Failure(rWrite.Error!);

        // Optional header stamping and compression policy application
        stampHeader?.Invoke(mf);
        if (compressionPolicy is not null)
        {
          var rPol = mf.ApplyCompressionPolicyToCurrentHdu(compressionPolicy);
          if (rPol.IsFailure) return Result<byte[]>.Failure(rPol.Error!);
        }

        return Result<byte[]>.Success(mf.ToArray());
      }
      catch (Exception ex)
      {
        return Result<byte[]>.Failure(ex);
      }
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
      var r = bitmap.ToFitsU16BytesResult(compressionPolicy, stampHeader);
      if (r.IsFailure) throw r.Error!;
      var bytes = r.Value;
      output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Result-based variant that writes directly to a stream.
    /// </summary>
    public static Result<bool> SaveAsFitsU16Result(this SKBitmap bitmap,
                                                   Stream output,
                                                   FitsCompressionPolicy? compressionPolicy = null,
                                                   Action<FitsFile>? stampHeader = null)
    {
      if (output == null) return Result<bool>.Failure(new ArgumentNullException(nameof(output)));
      var r = bitmap.ToFitsU16BytesResult(compressionPolicy, stampHeader);
      if (r.IsFailure) return Result<bool>.Failure(r.Error!);
      var bytes = r.Value;
      output.Write(bytes, 0, bytes.Length);
      return Result<bool>.Success(true);
    }

    /// <summary>
    /// Load a FITS image (from memory) into a new Gray8 <see cref="SKBitmap"/>.
    /// </summary>
    /// <param name="fitsBytes">FITS byte buffer.</param>
    /// <param name="preferU16">
    /// If true, reads as U16 then down-converts to Gray8 (keep dynamic range); else read as U8 if present.
    /// </param>

    /// <summary>
    /// Recompress a FITS file in-memory using CFITSIO's image-compress (all image HDUs).
    /// Uses defaults similar to fpack unless a custom policy is applied beforehand.
    /// </summary>
    public static byte[] RecompressFitsBytes(this byte[] fitsBytes)
    {
      var r = RecompressFitsBytesResult(fitsBytes);
      if (r.IsFailure) throw r.Error!;
      return r.Value;
    }

    /// <summary>
    /// Result-based variant for in-memory recompression.
    /// </summary>
    public static Result<byte[]> RecompressFitsBytesResult(this byte[] fitsBytes)
    {
      if (fitsBytes == null || fitsBytes.Length == 0)
        return Result<byte[]>.Failure(new ArgumentException("Empty FITS buffer.", nameof(fitsBytes)));

      try
      {
        using var src = FitsFile.OpenFromMemory(fitsBytes, readWrite: false);
        return Result<byte[]>.Success(src.CompressToArray());
      }
      catch (Exception ex)
      {
        return Result<byte[]>.Failure(ex);
      }
    }

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
