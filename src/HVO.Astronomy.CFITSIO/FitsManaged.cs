#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using HVO.Astronomy.CFITSIO.Interop;
using static HVO.Astronomy.CFITSIO.Interop.CFitsIO;
using HVO;

#if HAS_SKIA
using SkiaSharp;
#endif

namespace HVO.Astronomy.CFITSIO
{
  /// <summary>
  /// Compression algorithms supported by CFITSIO for tiled image compression.
  /// </summary>
  public enum FitsCompression
  {
    /// <summary>No compression.</summary>
    None = 0,

    /// <summary>Rice compression (RICE_1).</summary>
    Rice = CFitsIO.RICE_1,

    /// <summary>GZIP_1 compression (row-by-row, default variant).</summary>
    GZip1 = CFitsIO.GZIP_1,

    /// <summary>GZIP_2 compression (tiled variant).</summary>
    GZip2 = CFitsIO.GZIP_2,

    /// <summary>HCOMPRESS (wavelet-based) compression.</summary>
    HCompress = CFitsIO.HCOMPRESS_1,

    /// <summary>PLIO compression (integer-only, limited dynamic range).</summary>
    Plio = CFitsIO.PLIO_1
  }

  /// <summary>
  /// Policy for (re)compressing FITS images. For simple whole-file compression
  /// use <see cref="FitsFile.CompressTo(string)"/> (CFITSIO’s defaults).
  /// Use this policy with <see cref="FitsFile.ApplyCompressionPolicyToCurrentHdu(FitsCompressionPolicy)"/>
  /// when you need to control tiling/parameters per HDU.
  /// </summary>
  public sealed class FitsCompressionPolicy
  {
    /// <summary>Compression algorithm. Use <see cref="FitsCompression.None"/> for none.</summary>
    public FitsCompression Compression { get; init; } = FitsCompression.Rice;

    /// <summary>
    /// Optional tile dimensions. For 2D images: <c>{ tileWidth, tileHeight }</c>.
    /// Length should equal the number of axes.
    /// </summary>
    public long[]? TileDimensions { get; init; }

    /// <summary>
    /// Optional algorithm-specific parameters (e.g., HCOMPRESS scale factor).
    /// See CFITSIO documentation for valid parameters per algorithm.
    /// </summary>
    public float[]? Parameters { get; init; }

    /// <summary>If true, write HDU checksums after applying settings.</summary>
    public bool WriteChecksum { get; init; } = true;
  }

  /// <summary>
  /// Represents an open FITS file backed by a native <c>fitsfile*</c>.
  /// Use <see cref="Create(string)"/> or <see cref="Open(string, bool)"/>, and <see cref="Dispose"/> when finished.
  /// </summary>
  public sealed class FitsFile : IDisposable
  {
    /// <summary>
    /// The native handle for this FITS file. Disposed automatically when this <see cref="FitsFile"/> is disposed.
    /// </summary>
    public CFitsIO.SafeFitsFile Handle { get; }

    /// <summary>
    /// The original file path used to create or open this file (if known). Helpful in policy-driven recompress flows.
    /// </summary>
    public string? SourcePath { get; }

    private FitsFile(CFitsIO.SafeFitsFile handle, string? sourcePath)
    {
      Handle = handle ?? throw new ArgumentNullException(nameof(handle));
      SourcePath = sourcePath;
    }

    /// <summary>
    /// Create a new FITS file on disk.
    /// </summary>
    /// <param name="filePath">Destination path. Prefix with <c>!</c> to overwrite if it exists.</param>
    public static Result<FitsFile> Create(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath)) return Result<FitsFile>.Failure(new ArgumentNullException(nameof(filePath)));
      try
      {
        int status = 0;
        CFitsIO.fits_create_file(out var handle, filePath, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<FitsFile>.Success(new FitsFile(handle, Unbang(filePath)));
      }
      catch (Exception ex)
      {
        return Result<FitsFile>.Failure(ex);
      }
    }

    /// <summary>
    /// Open an existing FITS file.
    /// </summary>
    /// <param name="filePath">Path to an existing FITS file.</param>
    /// <param name="readWrite">If true, open read/write; otherwise read-only.</param>
    public static Result<FitsFile> Open(string filePath, bool readWrite = false)
    {
      if (string.IsNullOrWhiteSpace(filePath)) return Result<FitsFile>.Failure(new ArgumentNullException(nameof(filePath)));
      try
      {
        int status = 0;
        CFitsIO.fits_open_file(out var handle, filePath, readWrite ? CFitsIO.READWRITE : CFitsIO.READONLY, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<FitsFile>.Success(new FitsFile(handle, filePath));
      }
      catch (Exception ex)
      {
        return Result<FitsFile>.Failure(ex);
      }
    }

    /// <summary>
    /// Get the current HDU type and absolute number (1-based).
    /// </summary>
    public Result<(int HduType, int AbsoluteHduNumber)> GetCurrentHduInfo()
    {
      try
      {
        int status = 0;
        CFitsIO.fits_get_hdu_type(Handle, out int hduType, ref status);
        CFitsIO.ThrowIfError(status);

        CFitsIO.fits_get_hdu_num(Handle, out int absoluteHduNumber);
        return Result<(int, int)>.Success((hduType, absoluteHduNumber));
      }
      catch (Exception ex)
      {
        return Result<(int, int)>.Failure(ex);
      }
    }

    /// <summary>
    /// Move to the specified absolute HDU number (1-based). Returns the new HDU type.
    /// </summary>
    public Result<int> MoveToHdu(int absoluteHduNumber)
    {
      try
      {
        int status = 0;
        CFitsIO.fits_movabs_hdu(Handle, absoluteHduNumber, out int hduType, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<int>.Success(hduType);
      }
      catch (Exception ex)
      {
        return Result<int>.Failure(ex);
      }
    }

    /// <summary>
    /// Move by a relative number of HDUs (positive moves forward). Returns the new HDU type.
    /// </summary>
    public Result<int> MoveBy(int relativeHduOffset)
    {
      try
      {
        int status = 0;
        CFitsIO.fits_movrel_hdu(Handle, relativeHduOffset, out int hduType, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<int>.Success(hduType);
      }
      catch (Exception ex)
      {
        return Result<int>.Failure(ex);
      }
    }

    /// <summary>
    /// Get the total number of HDUs in this file.
    /// </summary>
    public Result<int> GetNumberOfHdus()
    {
      try
      {
        int status = 0;
        CFitsIO.fits_get_num_hdus(Handle, out int count, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<int>.Success(count);
      }
      catch (Exception ex)
      {
        return Result<int>.Failure(ex);
      }
    }

    /// <summary>
    /// Create a new image HDU (primary or extension). For unsigned 16-bit images, pass <see cref="CFitsIO.USHORT_IMG"/>.
    /// </summary>
    /// <param name="bitpix">CFITSIO BITPIX constant (e.g., <see cref="CFitsIO.USHORT_IMG"/>, <see cref="CFitsIO.FLOAT_IMG"/>).</param>
    /// <param name="axisLengths">Axis lengths; length equals the number of axes.</param>
    public Result<bool> CreateImageHdu(int bitpix, params long[] axisLengths)
    {
      if (axisLengths is null || axisLengths.Length == 0)
        return Result<bool>.Failure(new ArgumentException("At least one axis length is required.", nameof(axisLengths)));
      try
      {
        int status = 0;
        CFitsIO.fits_create_imgll(Handle, bitpix, axisLengths.Length, axisLengths, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>
    /// Get image parameters of the current HDU.
    /// </summary>
    /// <param name="maximumAxes">Maximum axes queried (allocates an array of this size).</param>
    /// <returns>(BITPIX, numberOfAxes, axisLengths)</returns>
    public Result<(int Bitpix, int NumberOfAxes, long[] AxisLengths)> GetImageParameters(int maximumAxes = 9)
    {
      try
      {
        int status = 0;
        var axis = new long[Math.Max(1, maximumAxes)];
        CFitsIO.fits_get_img_paramll(Handle, axis.Length, out int bitpix, out int naxis, axis, ref status);
        CFitsIO.ThrowIfError(status);
        if (naxis < axis.Length) Array.Resize(ref axis, naxis);
        return Result<(int, int, long[])>.Success((bitpix, naxis, axis));
      }
      catch (Exception ex)
      {
        return Result<(int, int, long[])>.Failure(ex);
      }
    }

    /// <summary>
    /// Set BSCALE and BZERO for the current image HDU. For unsigned 16-bit storage, the convention is BSCALE=1, BZERO=32768.
    /// </summary>
    public Result<bool> SetScale(double bScale, double bZero)
    {
      try
      {
        int status = 0;
        CFitsIO.fits_set_bscale(Handle, bScale, bZero, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>
    /// Write a linear block of pixels (1-based starting element index).
    /// </summary>
    /// <typeparam name="T">Unmanaged element type (<c>byte</c>, <c>ushort</c>, etc.).</typeparam>
    /// <param name="cfitsioTypeCode">CFITSIO type code for <typeparamref name="T"/> (e.g., <see cref="CFitsIO.TUSHORT"/>).</param>
    /// <param name="firstElementIndex">1-based element index in FITS linearized array.</param>
    /// <param name="source">Source pixel span.</param>
    public Result<bool> WritePixels<T>(int cfitsioTypeCode, long firstElementIndex, ReadOnlySpan<T> source)
        where T : unmanaged
    {
      try
      {
        int status = 0;
        unsafe
        {
          fixed (T* p = source)
          {
            CFitsIO.fits_write_img(
                Handle,
                cfitsioTypeCode,
                firstElementIndex,
                source.Length,
                (IntPtr)p,
                ref status);
          }
        }
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>
    /// Read a linear block of pixels (1-based starting element index) into <paramref name="destination"/>.
    /// </summary>
    /// <typeparam name="T">Unmanaged element type (<c>byte</c>, <c>ushort</c>, etc.).</typeparam>
    /// <param name="cfitsioTypeCode">CFITSIO type code for <typeparamref name="T"/>.</param>
    /// <param name="firstElementIndex">1-based element index in FITS linearized array.</param>
    /// <param name="destination">Destination pixel span.</param>
    public Result<bool> ReadPixels<T>(int cfitsioTypeCode, long firstElementIndex, Span<T> destination)
        where T : unmanaged
    {
      try
      {
        int status = 0;
        int anyNull;
        unsafe
        {
          fixed (T* p = destination)
          {
            CFitsIO.fits_read_img(
                Handle,
                cfitsioTypeCode,
                firstElementIndex,
                destination.Length,
                IntPtr.Zero,
                (IntPtr)p,
                out anyNull,
                ref status);
          }
        }
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>Strongly-typed helper to write <c>ushort</c> pixels.</summary>
  public Result<bool> WritePixelsU16(long firstElementIndex, ReadOnlySpan<ushort> source)
    => WritePixels<ushort>(CFitsIO.TUSHORT, firstElementIndex, source);

    /// <summary>Strongly-typed helper to write <c>byte</c> pixels.</summary>
  public Result<bool> WritePixelsU8(long firstElementIndex, ReadOnlySpan<byte> source)
    => WritePixels<byte>(CFitsIO.TBYTE, firstElementIndex, source);

    /// <summary>Strongly-typed helper to read <c>ushort</c> pixels.</summary>
  public Result<bool> ReadPixelsU16(long firstElementIndex, Span<ushort> destination)
    => ReadPixels<ushort>(CFitsIO.TUSHORT, firstElementIndex, destination);

    /// <summary>Strongly-typed helper to read <c>byte</c> pixels.</summary>
  public Result<bool> ReadPixelsU8(long firstElementIndex, Span<byte> destination)
    => ReadPixels<byte>(CFitsIO.TBYTE, firstElementIndex, destination);

    /// <summary>
    /// Write a raw 80-character header card (advanced; consider using typed helpers).
    /// </summary>
    public Result<bool> WriteHeaderCard(string card)
    {
      try
      {
        int status = 0;
        CFitsIO.fits_write_record(Handle, card, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>Delete a header keyword from the current HDU.</summary>
    public Result<bool> DeleteHeaderKey(string keyword)
    {
      try
      {
        int status = 0;
        CFitsIO.fits_delete_key(Handle, keyword, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>Create or update a string keyword on the current HDU.</summary>
    public Result<bool> WriteKeyString(string keyword, string value, string comment = "")
    {
      try
      {
        int status = 0;
        CFitsIO.fits_update_key_str(Handle, keyword, value, comment, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>Create or update a 32-bit integer keyword on the current HDU.</summary>
    public Result<bool> WriteKeyInt32(string keyword, int value, string comment = "")
    {
      try
      {
        int status = 0;
        CFitsIO.fits_update_key_lng(Handle, keyword, value, comment, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>Create or update a 64-bit integer keyword on the current HDU.</summary>
    public Result<bool> WriteKeyInt64(string keyword, long value, string comment = "")
    {
      try
      {
        int status = 0;
        CFitsIO.fits_update_key_lnglng(Handle, keyword, value, comment, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>Create or update a double-precision keyword on the current HDU.</summary>
    public Result<bool> WriteKeyDouble(string keyword, double value, int decimals = -1, string comment = "")
    {
      try
      {
        int status = 0;
        CFitsIO.fits_update_key_dbl(Handle, keyword, value, decimals, comment, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>Create or update a logical (boolean) keyword on the current HDU.</summary>
    public Result<bool> WriteKeyBoolean(string keyword, bool value, string comment = "")
    {
      try
      {
        int status = 0;
        CFitsIO.fits_update_key_log(Handle, keyword, value ? 1 : 0, comment, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>Try to read a string keyword value; returns null if not present.</summary>
    public Result<string?> TryGetKeyString(string keyword)
    {
      try
      {
        return Result<string?>.Success(CFitsIO.TryReadKeyString(Handle, keyword));
      }
      catch (Exception ex)
      {
        return Result<string?>.Failure(ex);
      }
    }

    /// <summary>Try to read a 32-bit integer keyword value; returns null if not present.</summary>
    public Result<int?> TryGetKeyInt32(string keyword)
    {
      try
      {
        int status = 0;
        int value = 0;
        CFitsIO.fits_read_key_lng(Handle, keyword, ref value, IntPtr.Zero, ref status);
        if (status != 0) return Result<int?>.Success(null);
        return Result<int?>.Success(value);
      }
      catch (Exception ex)
      {
        return Result<int?>.Failure(ex);
      }
    }

    /// <summary>Try to read a 64-bit integer keyword value; returns null if not present.</summary>
    public Result<long?> TryGetKeyInt64(string keyword)
    {
      try
      {
        int status = 0;
        long value = 0;
        CFitsIO.fits_read_key_lnglng(Handle, keyword, ref value, IntPtr.Zero, ref status);
        if (status != 0) return Result<long?>.Success(null);
        return Result<long?>.Success(value);
      }
      catch (Exception ex)
      {
        return Result<long?>.Failure(ex);
      }
    }

    /// <summary>Try to read a double-precision keyword value; returns null if not present.</summary>
    public Result<double?> TryGetKeyDouble(string keyword)
    {
      try
      {
        int status = 0;
        double value = 0;
        CFitsIO.fits_read_key_dbl(Handle, keyword, ref value, IntPtr.Zero, ref status);
        if (status != 0) return Result<double?>.Success(null);
        return Result<double?>.Success(value);
      }
      catch (Exception ex)
      {
        return Result<double?>.Failure(ex);
      }
    }

    /// <summary>Try to read a logical (boolean) keyword value; returns null if not present.</summary>
    public Result<bool?> TryGetKeyBoolean(string keyword)
    {
      try
      {
        int status = 0;
        int logical = 0;
        CFitsIO.fits_read_key_log(Handle, keyword, ref logical, IntPtr.Zero, ref status);
        if (status != 0) return Result<bool?>.Success(null);
        return Result<bool?>.Success(logical != 0);
      }
      catch (Exception ex)
      {
        return Result<bool?>.Failure(ex);
      }
    }

    /// <summary>
    /// Read all header cards from the current HDU as raw 80-character strings.
    /// </summary>
    public Result<IReadOnlyList<string>> ReadAllHeaderCards()
    {
      try
      {
        int status = 0;
        CFitsIO.fits_get_hdrspace(Handle, out int numberOfCards, out _, ref status);
        CFitsIO.ThrowIfError(status);

        var cards = new List<string>(numberOfCards);
        for (int i = 1; i <= numberOfCards; i++)
        {
          int s = 0;
          string card = CFitsIO.ReadRecordToString(Handle, i, ref s);
          CFitsIO.ThrowIfError(s);
          cards.Add(card);
        }
        return Result<IReadOnlyList<string>>.Success(cards);
      }
      catch (Exception ex)
      {
        return Result<IReadOnlyList<string>>.Failure(ex);
      }
    }

    /// <summary>
    /// File-wide compression using CFITSIO’s <c>fits_img_compress</c>.
    /// Compresses all image HDUs into a new file. Non-image HDUs are copied unchanged.
    /// </summary>
    /// <param name="outputPath">Output path (prefix with <c>!</c> to overwrite).</param>
    public Result<bool> CompressTo(string outputPath)
    {
      if (string.IsNullOrWhiteSpace(outputPath)) return Result<bool>.Failure(new ArgumentNullException(nameof(outputPath)));

      int status = 0;
      CFitsIO.fits_create_file(out var outHandle, outputPath, ref status);
      try
      {
        CFitsIO.ThrowIfError(status);
        CFitsIO.fits_img_compress(Handle, outHandle, ref status);
        CFitsIO.ThrowIfError(status);
        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
      finally
      {
        outHandle.Dispose();
      }
    }

    /// <summary>
    /// Apply compression settings (algorithm, tiles, parameters) to the <b>current HDU of this file</b>.
    /// Use when building compressed image HDUs manually (advanced scenarios).
    /// </summary>
    public Result<bool> ApplyCompressionPolicyToCurrentHdu(FitsCompressionPolicy policy)
    {
      if (policy is null) return Result<bool>.Failure(new ArgumentNullException(nameof(policy)));
      try
      {
        int status = 0;

        if (policy.Compression != FitsCompression.None)
        {
          CFitsIO.fits_set_compression_type(Handle, (int)policy.Compression, ref status);
          CFitsIO.ThrowIfError(status);
        }

        if (policy.TileDimensions is { Length: > 0 })
        {
          CFitsIO.fits_set_tile_dimll(Handle, policy.TileDimensions.Length, policy.TileDimensions, ref status);
          CFitsIO.ThrowIfError(status);
        }

        if (policy.Parameters is { Length: > 0 })
        {
          CFitsIO.fits_set_compression_param(Handle, policy.Parameters.Length, policy.Parameters, ref status);
          CFitsIO.ThrowIfError(status);
        }

        if (policy.WriteChecksum)
        {
          CFitsIO.fits_write_chksum(Handle, ref status);
          CFitsIO.ThrowIfError(status);
        }

        return Result<bool>.Success(true);
      }
      catch (Exception ex)
      {
        return Result<bool>.Failure(ex);
      }
    }

    /// <summary>
    /// Dispose the file and close the underlying native handle. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
      Handle.Dispose();
    }

    private static string? Unbang(string path) => path?.StartsWith("!", StringComparison.Ordinal) == true ? path[1..] : path;
  }

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

  /// <summary>
  /// Common FITS keyword names and small helpers.
  /// </summary>
  public static class FitsCommonKeywords
  {
    // Structural
    public const string SIMPLE = "SIMPLE";
    public const string BITPIX = "BITPIX";
    public const string NAXIS = "NAXIS";
    public const string NAXIS1 = "NAXIS1";
    public const string NAXIS2 = "NAXIS2";
    public const string EXTEND = "EXTEND";
    public const string BSCALE = "BSCALE";
    public const string BZERO = "BZERO";
    public const string BUNIT = "BUNIT";

    // Timing/observation
    public const string DATE = "DATE";
    public const string DATEOBS = "DATE-OBS";
    public const string MJDOBS = "MJD-OBS";
    public const string EXPTIME = "EXPTIME";

    // WCS — 2D subset
    public const string CTYPE1 = "CTYPE1";
    public const string CTYPE2 = "CTYPE2";
    public const string CRVAL1 = "CRVAL1";
    public const string CRVAL2 = "CRVAL2";
    public const string CRPIX1 = "CRPIX1";
    public const string CRPIX2 = "CRPIX2";
    public const string CDELT1 = "CDELT1";
    public const string CDELT2 = "CDELT2";
    public const string CUNIT1 = "CUNIT1";
    public const string CUNIT2 = "CUNIT2";
    public const string CD1_1 = "CD1_1";
    public const string CD1_2 = "CD1_2";
    public const string CD2_1 = "CD2_1";
    public const string CD2_2 = "CD2_2";
    public const string LONPOLE = "LONPOLE";
    public const string LATPOLE = "LATPOLE";
    public const string EQUINOX = "EQUINOX";
    public const string RADESYS = "RADESYS";

    /// <summary>Return ISO-8601 timestamp suitable for DATE-OBS.</summary>
    public static string IsoTimestamp(DateTime utc) =>
        utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
  }

  /// <summary>
  /// Fluent helper for stamping common header keywords.
  /// </summary>
  public sealed class FitsHeaderBuilder
  {
    private readonly FitsFile _fits;

    /// <summary>Create a new header builder bound to <paramref name="fits"/>.</summary>
  public FitsHeaderBuilder(FitsFile fits) => _fits = fits ?? throw new ArgumentNullException(nameof(fits));

    /// <summary>Set an arbitrary string keyword.</summary>
    public FitsHeaderBuilder SetString(string keyword, string value, string comment = "")
    {
      _fits.WriteKeyString(keyword, value, comment);
      return this;
    }

    /// <summary>Set a 32-bit integer keyword.</summary>
    public FitsHeaderBuilder SetInt32(string keyword, int value, string comment = "")
    {
      _fits.WriteKeyInt32(keyword, value, comment);
      return this;
    }

    /// <summary>Set a 64-bit integer keyword.</summary>
    public FitsHeaderBuilder SetInt64(string keyword, long value, string comment = "")
    {
      _fits.WriteKeyInt64(keyword, value, comment);
      return this;
    }

    /// <summary>Set a double-precision keyword.</summary>
    public FitsHeaderBuilder SetDouble(string keyword, double value, int decimals = -1, string comment = "")
    {
      _fits.WriteKeyDouble(keyword, value, decimals, comment);
      return this;
    }

    /// <summary>Set a logical (boolean) keyword.</summary>
    public FitsHeaderBuilder SetBoolean(string keyword, bool value, string comment = "")
    {
      _fits.WriteKeyBoolean(keyword, value, comment);
      return this;
    }

    /// <summary>Stamp DATE with current UTC.</summary>
    public FitsHeaderBuilder StampCurrentDate() { int s = 0; CFitsIO.fits_write_date(_fits.Handle, ref s); CFitsIO.ThrowIfError(s); return this; }

    /// <summary>Set DATE-OBS in ISO-8601 from a UTC timestamp.</summary>
    public FitsHeaderBuilder SetDateObs(DateTime utc)
    {
      _fits.WriteKeyString(FitsCommonKeywords.DATEOBS, FitsCommonKeywords.IsoTimestamp(utc), "Start of observation (UTC)");
      return this;
    }

    /// <summary>Set exposure time in seconds.</summary>
    public FitsHeaderBuilder SetExposureSeconds(double seconds)
    {
      _fits.WriteKeyDouble(FitsCommonKeywords.EXPTIME, seconds, -1, "Exposure time (s)");
      return this;
    }

    /// <summary>Set BSCALE/BZERO explicitly.</summary>
    public FitsHeaderBuilder SetScale(double bScale, double bZero)
    {
      _fits.SetScale(bScale, bZero);
      return this;
    }
  }

  /// <summary>
  /// Fluent helper for stamping 2D WCS keywords (RA/Dec TAN by default).
  /// </summary>
  public sealed class WcsHeaderBuilder
  {
    private readonly FitsFile _fits;

    /// <summary>Create a new WCS builder bound to <paramref name="fits"/>.</summary>
    public WcsHeaderBuilder(FitsFile fits) => _fits = fits ?? throw new ArgumentNullException(nameof(fits));

    /// <summary>
    /// Set a simple TAN (gnomonic) WCS using pixel scale (degrees per pixel) and reference pixel/value.
    /// Uses CDELT1/CDELT2 and CTYPE1/2 = RA---TAN / DEC--TAN. Positive CDELT2; CDELT1 often negative for RA.
    /// </summary>
    public WcsHeaderBuilder SetSimpleTan(
        double referenceWorldLongitudeDegrees,
        double referenceWorldLatitudeDegrees,
        double referencePixelX,
        double referencePixelY,
        double degreesPerPixelX,
        double degreesPerPixelY,
        string unitsAxis1 = "deg",
        string unitsAxis2 = "deg")
    {
      _fits.WriteKeyString(FitsCommonKeywords.CTYPE1, "RA---TAN", "WCS projection");
      _fits.WriteKeyString(FitsCommonKeywords.CTYPE2, "DEC--TAN", "WCS projection");
      _fits.WriteKeyDouble(FitsCommonKeywords.CRVAL1, referenceWorldLongitudeDegrees, -1, "Reference world longitude (deg)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CRVAL2, referenceWorldLatitudeDegrees, -1, "Reference world latitude (deg)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CRPIX1, referencePixelX, -1, "Reference pixel X (1-based)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CRPIX2, referencePixelY, -1, "Reference pixel Y (1-based)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CDELT1, degreesPerPixelX, -1, "Degrees per pixel (X)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CDELT2, degreesPerPixelY, -1, "Degrees per pixel (Y)");
      _fits.WriteKeyString(FitsCommonKeywords.CUNIT1, unitsAxis1, "Axis 1 units");
      _fits.WriteKeyString(FitsCommonKeywords.CUNIT2, unitsAxis2, "Axis 2 units");
      return this;
    }

    /// <summary>
    /// Set a TAN WCS using a 2x2 CD matrix (degrees per pixel with rotation/shear).
    /// </summary>
    public WcsHeaderBuilder SetTanWithCdMatrix(
        double referenceWorldLongitudeDegrees,
        double referenceWorldLatitudeDegrees,
        double referencePixelX,
        double referencePixelY,
        double cd11, double cd12, double cd21, double cd22,
        string unitsAxis1 = "deg",
        string unitsAxis2 = "deg")
    {
      _fits.WriteKeyString(FitsCommonKeywords.CTYPE1, "RA---TAN", "WCS projection");
      _fits.WriteKeyString(FitsCommonKeywords.CTYPE2, "DEC--TAN", "WCS projection");
      _fits.WriteKeyDouble(FitsCommonKeywords.CRVAL1, referenceWorldLongitudeDegrees, -1, "Reference world longitude (deg)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CRVAL2, referenceWorldLatitudeDegrees, -1, "Reference world latitude (deg)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CRPIX1, referencePixelX, -1, "Reference pixel X (1-based)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CRPIX2, referencePixelY, -1, "Reference pixel Y (1-based)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CD1_1, cd11, -1, "CD matrix 1,1 (deg/pix)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CD1_2, cd12, -1, "CD matrix 1,2 (deg/pix)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CD2_1, cd21, -1, "CD matrix 2,1 (deg/pix)");
      _fits.WriteKeyDouble(FitsCommonKeywords.CD2_2, cd22, -1, "CD matrix 2,2 (deg/pix)");
      _fits.WriteKeyString(FitsCommonKeywords.CUNIT1, unitsAxis1, "Axis 1 units");
      _fits.WriteKeyString(FitsCommonKeywords.CUNIT2, unitsAxis2, "Axis 2 units");
      return this;
    }

    /// <summary>
    /// Optional celestial system details (reference frame, equinox, pole definitions).
    /// </summary>
    public WcsHeaderBuilder SetCelestialSystem(string referenceFrame = "ICRS", double? equinox = null, double? longitudePole = null, double? latitudePole = null)
    {
      if (!string.IsNullOrWhiteSpace(referenceFrame))
        _fits.WriteKeyString(FitsCommonKeywords.RADESYS, referenceFrame, "Celestial reference frame");

      if (equinox.HasValue)
        _fits.WriteKeyDouble(FitsCommonKeywords.EQUINOX, equinox.Value, -1, "Equinox (year)");

      if (longitudePole.HasValue)
        _fits.WriteKeyDouble(FitsCommonKeywords.LONPOLE, longitudePole.Value, -1, "Native longitude of celestial pole");

      if (latitudePole.HasValue)
        _fits.WriteKeyDouble(FitsCommonKeywords.LATPOLE, latitudePole.Value, -1, "Native latitude of celestial pole");

      return this;
    }
  }

#if HAS_SKIA
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
}
