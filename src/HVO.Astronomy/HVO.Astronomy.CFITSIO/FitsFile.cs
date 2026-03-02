#nullable enable
using System;
using HVO.Core.Results;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HVO.Astronomy.CFITSIO.Interop;

namespace HVO.Astronomy.CFITSIO;

/// <summary>
/// Managed FITS file wrapper with typed Result&lt;T&gt; API for image I/O, header manipulation, and compression.
/// </summary>
public sealed partial class FitsFile : IDisposable
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

  /// <summary>
  /// Create or update a 64-bit integer keyword on the current HDU.
  /// NOTE: CFITSIO does not have native 64-bit integer support for keywords.
  /// This method stores the value as a string to preserve full precision.
  /// </summary>
  public Result<bool> WriteKeyInt64(string keyword, long value, string comment = "")
  {
    try
    {
      // CFITSIO doesn't support 64-bit integer keywords natively
      // Store as string to preserve full precision
      int status = 0;
      string valueStr = value.ToString();
      CFitsIO.fits_update_key_str(Handle, keyword, valueStr, comment, ref status);
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

  /// <summary>
  /// Try to read a 64-bit integer keyword value; returns null if not present.
  /// NOTE: CFITSIO does not have native 64-bit integer support.
  /// This reads the value as a string and parses it to preserve full precision.
  /// </summary>
  public Result<long?> TryGetKeyInt64(string keyword)
  {
    try
    {
      // Read as string since CFITSIO doesn't support 64-bit integers natively
      var stringResult = TryGetKeyString(keyword);
      if (stringResult.IsFailure) return Result<long?>.Failure(stringResult.Error!);
      if (stringResult.Value == null) return Result<long?>.Success(null);

      // Parse the string to long
      if (long.TryParse(stringResult.Value, out long value))
      {
        return Result<long?>.Success(value);
      }
      return Result<long?>.Failure(new FormatException($"Keyword '{keyword}' value '{stringResult.Value}' is not a valid Int64"));
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
  /// File-wide compression using CFITSIO's <c>fits_img_compress</c>.
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

      // NOTE: fits_set_tile_dimll and fits_set_compression_param are not available in all CFITSIO builds.
      // These settings are commented out; use fits_set_compression_type for basic compression control.

      // if (policy.TileDimensions is { Length: > 0 })
      // {
      //   CFitsIO.fits_set_tile_dimll(Handle, policy.TileDimensions.Length, policy.TileDimensions, ref status);
      //   CFitsIO.ThrowIfError(status);
      // }

      // if (policy.Parameters is { Length: > 0 })
      // {
      //   CFitsIO.fits_set_compression_param(Handle, policy.Parameters.Length, policy.Parameters, ref status);
      //   CFitsIO.ThrowIfError(status);
      // }

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
    try
    {
      Handle.Dispose();
    }
    finally
    {
      // Clean up memory resources (control blocks and external buffers)
      DisposeMemoryResources();
    }
  }


  private static string? Unbang(string path) => path?.StartsWith("!", StringComparison.Ordinal) == true ? path[1..] : path;
}
