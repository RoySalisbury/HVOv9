#nullable enable
using System;
using HVO.Core.Results;
using HVO.Astronomy.CFITSIO.Interop;

namespace HVO.Astronomy.CFITSIO;

/// <summary>
/// High-level helpers to write/read common grayscale images to/from FITS.
/// </summary>
public static class FitsImage
{
  /// <summary>
  /// Create a new unsigned 16-bit grayscale (0..65535) image HDU and write all pixels (row-major).
  /// Sets BSCALE=1 and BZERO=32768.
  /// </summary>
  public static Result<bool> WriteU16(FitsFile fits, int width, int height, ReadOnlySpan<ushort> pixelsRowMajor)
  {
    if (fits is null) return Result<bool>.Failure(new ArgumentNullException(nameof(fits)));
    if (pixelsRowMajor.Length != checked(width * height))
      return Result<bool>.Failure(new ArgumentException("Pixel buffer length does not match width*height.", nameof(pixelsRowMajor)));

    var r1 = fits.CreateImageHdu(CFitsIO.USHORT_IMG, width, height);
    if (r1.IsFailure) return Result<bool>.Failure(r1.Error!);

    var r2 = fits.SetScale(1.0, 32768.0);
    if (r2.IsFailure) return Result<bool>.Failure(r2.Error!);

    var r3 = fits.WritePixelsU16(1, pixelsRowMajor);
    if (r3.IsFailure) return Result<bool>.Failure(r3.Error!);

    // Non-critical header writes; propagate failure to be consistent with Result-based API
    var r4 = fits.WriteKeyString("BUNIT", "ADU", "Pixel units");
    if (r4.IsFailure) return Result<bool>.Failure(r4.Error!);
    var r5 = fits.WriteKeyString("BITDEPTH", "16", "Unsigned 16-bit pixels");
    if (r5.IsFailure) return Result<bool>.Failure(r5.Error!);

    return Result<bool>.Success(true);
  }

  /// <summary>
  /// Create a new 8-bit grayscale image HDU and write all pixels (row-major).
  /// </summary>
  public static Result<bool> WriteU8(FitsFile fits, int width, int height, ReadOnlySpan<byte> pixelsRowMajor)
  {
    if (fits is null) return Result<bool>.Failure(new ArgumentNullException(nameof(fits)));
    if (pixelsRowMajor.Length != checked(width * height))
      return Result<bool>.Failure(new ArgumentException("Pixel buffer length does not match width*height.", nameof(pixelsRowMajor)));

    var r1 = fits.CreateImageHdu(CFitsIO.BYTE_IMG, width, height);
    if (r1.IsFailure) return Result<bool>.Failure(r1.Error!);

    var r2 = fits.WritePixelsU8(1, pixelsRowMajor);
    if (r2.IsFailure) return Result<bool>.Failure(r2.Error!);

    var r3 = fits.WriteKeyString("BUNIT", "ADU", "Pixel units");
    if (r3.IsFailure) return Result<bool>.Failure(r3.Error!);
    var r4 = fits.WriteKeyString("BITDEPTH", "8", "Unsigned 8-bit pixels");
    if (r4.IsFailure) return Result<bool>.Failure(r4.Error!);

    return Result<bool>.Success(true);
  }

  /// <summary>
  /// Read the current image HDU as unsigned 16-bit grayscale (row-major).
  /// </summary>
  public static Result<(ushort[] Pixels, int Width, int Height)> ReadU16(FitsFile fits)
  {
    if (fits is null) return Result<(ushort[] Pixels, int Width, int Height)>.Failure(new ArgumentNullException(nameof(fits)));

    var ip = fits.GetImageParameters();
    if (ip.IsFailure) return Result<(ushort[] Pixels, int Width, int Height)>.Failure(ip.Error!);
    var (_, naxis, naxes) = ip.Value;
    if (naxis < 2) return Result<(ushort[] Pixels, int Width, int Height)>.Failure(new InvalidOperationException("Current HDU is not a 2D image."));

    int width = checked((int)naxes[0]);
    int height = checked((int)naxes[1]);

    var buffer = new ushort[checked(width * height)];
    var rr = fits.ReadPixelsU16(1, buffer);
    if (rr.IsFailure) return Result<(ushort[] Pixels, int Width, int Height)>.Failure(rr.Error!);
    return Result<(ushort[] Pixels, int Width, int Height)>.Success((buffer, width, height));
  }

  /// <summary>
  /// Read the current image HDU as unsigned 8-bit grayscale (row-major).
  /// </summary>
  public static Result<(byte[] Pixels, int Width, int Height)> ReadU8(FitsFile fits)
  {
    if (fits is null) return Result<(byte[] Pixels, int Width, int Height)>.Failure(new ArgumentNullException(nameof(fits)));

    var ip = fits.GetImageParameters();
    if (ip.IsFailure) return Result<(byte[] Pixels, int Width, int Height)>.Failure(ip.Error!);
    var (_, naxis, naxes) = ip.Value;
    if (naxis < 2) return Result<(byte[] Pixels, int Width, int Height)>.Failure(new InvalidOperationException("Current HDU is not a 2D image."));

    int width = checked((int)naxes[0]);
    int height = checked((int)naxes[1]);

    var buffer = new byte[checked(width * height)];
    var rr = fits.ReadPixelsU8(1, buffer);
    if (rr.IsFailure) return Result<(byte[] Pixels, int Width, int Height)>.Failure(rr.Error!);
    return Result<(byte[] Pixels, int Width, int Height)>.Success((buffer, width, height));
  }
}
