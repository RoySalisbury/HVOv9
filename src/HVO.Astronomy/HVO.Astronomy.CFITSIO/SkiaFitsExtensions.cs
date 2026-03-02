#nullable enable
using System;
using HVO.Core.Results;
using System.IO;
using HVO.Astronomy.CFITSIO.Interop;

#if HAS_SKIA
using SkiaSharp;

namespace HVO.Astronomy.CFITSIO;

/// <summary>
/// SkiaSharp extension helpers for converting to/from FITS.
/// Define the symbol <c>HAS_SKIA</c> and reference SkiaSharp to enable.
/// All bitmaps generated here are Gray8 for portability (SkiaSharp rarely exposes Gray16).
/// </summary>
public static class SkiaFitsExtensions
{
  /// <summary>
  /// Save a grayscale <see cref="SKBitmap"/> as a 16-bit FITS image (0..65535).
  /// If not Gray8, pixels are converted to Gray8, then expanded to U16 (replicated byte).
  /// </summary>
  public static Result<bool> SaveAsFitsU16(this SKBitmap bitmap,
                                           string fitsPath,
                                           bool overwrite = true,
                                           FitsCompressionPolicy? compressionPolicy = null,
                                           Action<FitsFile>? stampHeader = null)
  {
    if (bitmap is null) return Result<bool>.Failure(new ArgumentNullException(nameof(bitmap)));
    if (string.IsNullOrWhiteSpace(fitsPath)) return Result<bool>.Failure(new ArgumentNullException(nameof(fitsPath)));

    // Extract a U16 plane (row-major)
    var (width, height, plane) = ExtractGrayU16(bitmap);

    // Create FITS and write image
    var rCreate = overwrite ? FitsFile.Create("!" + fitsPath) : FitsFile.Create(fitsPath);
    if (rCreate.IsFailure) return Result<bool>.Failure(rCreate.Error!);
    using var fits = rCreate.Value;
    var rWrite = FitsImage.WriteU16(fits, width, height, plane);
    if (rWrite.IsFailure) return Result<bool>.Failure(rWrite.Error!);

    // Optional header stamping (e.g., WCS)
    stampHeader?.Invoke(fits);

    // For simple whole-file compression (defaults), reopen and call CompressTo:
    // using var reopen = FitsFile.Open(fitsPath, readWrite: false);
    // reopen.CompressTo((overwrite ? "!" : "") + fitsPath);
    //
    // For fully custom tiling/parameters, build compressed HDUs manually and call:
    // fits.ApplyCompressionPolicyToCurrentHdu(compressionPolicy) before writing pixels.
    if (compressionPolicy is not null)
    {
      var rPol = fits.ApplyCompressionPolicyToCurrentHdu(compressionPolicy);
      if (rPol.IsFailure) return Result<bool>.Failure(rPol.Error!);
    }

    return Result<bool>.Success(true);
  }

  /// <summary>
  /// Save an <see cref="SKImage"/> as a 16-bit FITS image (0..65535).
  /// Internally snapshots to a temporary <see cref="SKBitmap"/>.
  /// </summary>
  public static Result<bool> SaveAsFitsU16(this SKImage image,
                                           string fitsPath,
                                           bool overwrite = true,
                                           FitsCompressionPolicy? compressionPolicy = null,
                                           Action<FitsFile>? stampHeader = null)
  {
    if (image is null) return Result<bool>.Failure(new ArgumentNullException(nameof(image)));
    using var bmp = SKBitmap.FromImage(image);
    return SaveAsFitsU16(bmp, fitsPath, overwrite, compressionPolicy, stampHeader);
  }

  /// <summary>
  /// Load a 2D FITS image into a new <see cref="SKBitmap"/> as Gray8.
  /// If <paramref name="preferU16"/> is true, the FITS is read as U16 and down-converted to Gray8 (>> 8).
  /// Otherwise it is read as U8 directly.
  /// </summary>
  /// <param name="fitsPath">Path to a FITS file.</param>
  /// <param name="preferU16">If true, downconvert U16 to Gray8; otherwise read as U8.</param>
  public static Result<SKBitmap> LoadFitsToBitmap(string fitsPath, bool preferU16 = true)
  {
    var ropen = FitsFile.Open(fitsPath, readWrite: false);
    if (ropen.IsFailure) return Result<SKBitmap>.Failure(ropen.Error!);
    using var ff = ropen.Value;
    var ip = ff.GetImageParameters();
    if (ip.IsFailure) return Result<SKBitmap>.Failure(ip.Error!);
    var (_, naxis, naxes) = ip.Value;
    if (naxis < 2) return Result<SKBitmap>.Failure(new InvalidOperationException("Not a 2D image."));

    int width = checked((int)naxes[0]);
    int height = checked((int)naxes[1]);

    var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
    var dst = bmp.GetPixelSpan();

    if (preferU16)
    {
      // Read as U16, then downconvert to Gray8 by dropping low 8 bits.
      var r16 = FitsImage.ReadU16(ff);
      if (r16.IsFailure) return Result<SKBitmap>.Failure(r16.Error!);
      var (pixels16, _, _) = r16.Value;
      for (int i = 0; i < pixels16.Length; i++)
        dst[i] = (byte)(pixels16[i] >> 8);
    }
    else
    {
      // Read as U8 directly.
      var r8 = FitsImage.ReadU8(ff);
      if (r8.IsFailure) return Result<SKBitmap>.Failure(r8.Error!);
      var (pixels8, _, _) = r8.Value;
      pixels8.CopyTo(dst);
    }

    return Result<SKBitmap>.Success(bmp);
  }

  /// <summary>
  /// Convenience: save a bitmap to PNG on disk.
  /// </summary>
  public static void SavePng(this SKBitmap bitmap, string pngPath, int pngQuality = 100)
  {
    if (bitmap is null) throw new ArgumentNullException(nameof(bitmap));
    if (string.IsNullOrWhiteSpace(pngPath)) throw new ArgumentNullException(nameof(pngPath));

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, pngQuality);
    using var fs = File.Open(pngPath, FileMode.Create, FileAccess.Write, FileShare.None);
    data.SaveTo(fs);
  }

  /// <summary>
  /// Convenience: save a bitmap to JPEG on disk.
  /// </summary>
  public static void SaveJpeg(this SKBitmap bitmap, string jpegPath, int jpegQuality = 90)
  {
    if (bitmap is null) throw new ArgumentNullException(nameof(bitmap));
    if (string.IsNullOrWhiteSpace(jpegPath)) throw new ArgumentNullException(nameof(jpegPath));
    if (jpegQuality < 1 || jpegQuality > 100) throw new ArgumentOutOfRangeException(nameof(jpegQuality));

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Jpeg, jpegQuality);
    using var fs = File.Open(jpegPath, FileMode.Create, FileAccess.Write, FileShare.None);
    data.SaveTo(fs);
  }

  /// <summary>
  /// Extract a 16-bit grayscale plane from a bitmap. If the bitmap is Gray8, expands to 16-bit by byte replication.
  /// Other types are converted to Gray8 via <see cref="SKImage.ReadPixels(SkiaSharp.SKImageInfo, nint, int, int, int)"/> then expanded.
  /// </summary>
  private static (int Width, int Height, ushort[] Plane) ExtractGrayU16(SKBitmap bitmap)
  {
    var info = bitmap.Info;
    int width = info.Width;
    int height = info.Height;

    if (info.ColorType == SKColorType.Gray8)
    {
      // Expand Gray8 → U16 by replicating the byte.
      var src = bitmap.GetPixelSpan();
      var u16 = new ushort[checked(width * height)];
      for (int i = 0; i < src.Length; i++)
      {
        byte v = src[i];
        u16[i] = (ushort)(v << 8 | v);
      }
      return (width, height, u16);
    }

    // Convert to Gray8 first using ReadPixels, then expand to U16.
    var grayInfo = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
    using var grayBmp = new SKBitmap();
    if (!grayBmp.TryAllocPixels(grayInfo))
      throw new InvalidOperationException("Failed to allocate gray bitmap.");

    using (var img = SKImage.FromBitmap(bitmap))
    using (var grayPix = grayBmp.PeekPixels() ?? throw new InvalidOperationException("Unable to access pixel data."))
    {
      bool ok = img.ReadPixels(grayInfo, grayPix.GetPixels(), grayPix.RowBytes, 0, 0);
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
#endif
