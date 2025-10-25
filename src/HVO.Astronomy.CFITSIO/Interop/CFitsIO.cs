#nullable enable
using System;
using System.Runtime.CompilerServices; // CallConvCdecl
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HVO.Astronomy.CFITSIO.Interop
{
  /// <summary>
  /// Source-generated P/Invoke surface for the native <c>CFITSIO</c> library (cdecl, UTF-8).
  /// Exports are bound to the canonical short symbol names (e.g., <c>ffopen</c>) that most
  /// CFITSIO builds expose. See the CFITSIO User’s Guide and <c>longnam.h</c> for mappings.
  /// </summary>
  public static partial class CFitsIO
  {
    private const string NativeLibraryName = "cfitsio";

    // Official FLEN_* from fitsio.h (Aug 2024 guide; older headers similar)
    private const int FLEN_FILENAME = 1025;
    private const int FLEN_KEYWORD = 72;
    private const int FLEN_CARD = 81;
    private const int FLEN_VALUE = 71;
    private const int FLEN_COMMENT = 73;
    private const int FLEN_ERRMSG = 81;
    private const int FLEN_STATUS = 31;

    // ───────────── SafeHandle for fitsfile* ─────────────
    /// <summary>Safe wrapper for <c>fitsfile*</c>. Disposing closes via <c>ffclos</c>.</summary>
    public sealed class SafeFitsFile : SafeHandleZeroOrMinusOneIsInvalid
    {
      public SafeFitsFile() : base(ownsHandle: true) { }
      protected override bool ReleaseHandle()
      {
        int status = 0;
        ffclos(handle, ref status);
        handle = IntPtr.Zero;
        return true;
      }
    }

    // ───────────── Exception helpers ─────────────
    public sealed class FitsInteropException : InvalidOperationException
    {
      public int Status { get; }
      public FitsInteropException(int status, string message)
        : base($"CFITSIO error {status}: {message}") => Status = status;
    }

    public static void ThrowIfError(int status)
    {
      if (status == 0) return;
      string summary = GetErrorStatusText(status);
      string stack = DrainErrorStack();
      string msg = string.IsNullOrWhiteSpace(stack) ? summary : $"{summary}{Environment.NewLine}{stack}";
      throw new FitsInteropException(status, msg);
    }

    public static unsafe string GetErrorStatusText(int status)
    {
      byte* buf = stackalloc byte[FLEN_STATUS];
      ffgerr(status, buf);
      return Utf8ZToString(buf);
    }

    public static unsafe string DrainErrorStack()
    {
      var sb = new StringBuilder();
      byte* line = stackalloc byte[FLEN_ERRMSG];
      while (ffgmsg(line) != 0) sb.AppendLine(Utf8ZToString(line));
      return sb.ToString();
    }

    // ───────────── Common constants ─────────────
    public const int READONLY = 0;
    public const int READWRITE = 1;

    public const int IMAGE_HDU = 0;
    public const int ASCII_TBL = 1;
    public const int BINARY_TBL = 2;
    public const int ANY_HDU = -1;

    public const int BYTE_IMG = 8;
    public const int SHORT_IMG = 16; // signed; use BSCALE/BZERO for U16
    public const int USHORT_IMG = 20; // pseudo-BITPIX (convention)
    public const int LONG_IMG = 32;
    public const int LONGLONG_IMG = 64;
    public const int FLOAT_IMG = -32;
    public const int DOUBLE_IMG = -64;

    public const int TBIT = 1;
    public const int TBYTE = 11;
    public const int TLOGICAL = 14;
    public const int TSTRING = 16;
    public const int TUSHORT = 20;
    public const int TSHORT = 21;
    public const int TUINT = 30;
    public const int TINT = 31;
    public const int TULONG = 40;
    public const int TLONG = 41;
    public const int TFLOAT = 42;
    public const int TDOUBLE = 82;
    public const int TLONGLONG = 81;

    public const int RICE_1 = 11;
    public const int GZIP_1 = 21;
    public const int GZIP_2 = 22;
    public const int PLIO_1 = 31;
    public const int HCOMPRESS_1 = 41;

    // ───────────── Files (ff*) ─────────────
    [LibraryImport(NativeLibraryName, EntryPoint = "ffopen", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_open_file(out SafeFitsFile fptr, string fileName, int ioMode, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffinit", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_create_file(out SafeFitsFile fptr, string fileName, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffclos")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int ffclos(IntPtr fptr, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffdelt")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_delete_file(SafeFitsFile fptr, ref int status);

    // Some builds still export ffvers (no fits_* alias).
    [LibraryImport(NativeLibraryName, EntryPoint = "ffvers")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void fits_get_version(out double version);

    // ───────────── HDU nav / info ─────────────
    [LibraryImport(NativeLibraryName, EntryPoint = "ffmahd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_movabs_hdu(SafeFitsFile fptr, int absoluteHduNumber, out int hduType, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffmrhd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_movrel_hdu(SafeFitsFile fptr, int relativeHduOffset, out int hduType, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffghdn")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_get_hdu_num(SafeFitsFile fptr, out int absoluteHduNumber);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffthdu")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_get_num_hdus(SafeFitsFile fptr, out int numberOfHdus, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffghdt")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_get_hdu_type(SafeFitsFile fptr, out int hduType, ref int status);

    // ───────────── Image create / query ─────────────
    [LibraryImport(NativeLibraryName, EntryPoint = "ffcrimll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_create_imgll(SafeFitsFile fptr, int bitpix, int numberOfAxes, long[] axisLengths, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffiimgll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_insert_imgll(SafeFitsFile fptr, int bitpix, int numberOfAxes, long[] axisLengths, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgiprll")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_get_img_paramll(SafeFitsFile fptr, int maxdim, out int bitpix, out int naxis, long[] naxes, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffpscl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_set_bscale(SafeFitsFile fptr, double bScale, double bZero, ref int status);

    // ───────────── Image I/O ─────────────
    // fits_write_img / ffppr
    [LibraryImport(NativeLibraryName, EntryPoint = "ffppr")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_img(SafeFitsFile fptr, int dataType, long firstElem, long nElem, IntPtr src, ref int status);

    // fits_read_img / ffgpv
    [LibraryImport(NativeLibraryName, EntryPoint = "ffgpv")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_read_img(SafeFitsFile fptr, int dataType, long firstElem, long nElem, IntPtr nullVal, IntPtr dest, out int anyNull, ref int status);

    // fits_write_subset / ffpss
    [LibraryImport(NativeLibraryName, EntryPoint = "ffpss")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_subset(SafeFitsFile fptr, int dataType, long[] firstPixel, long[] lastPixel, IntPtr src, ref int status);

    // fits_read_subset / ffgsv
    [LibraryImport(NativeLibraryName, EntryPoint = "ffgsv")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_read_subset(SafeFitsFile fptr, int dataType, long[] firstPixel, long[] lastPixel, long[]? inc, IntPtr nullVal, IntPtr dest, out int anyNull, ref int status);

    // ───────────── Headers / Keywords ─────────────
    [LibraryImport(NativeLibraryName, EntryPoint = "ffghsp")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_get_hdrspace(SafeFitsFile fptr, out int nkeys, out int keypos, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffghps")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_get_hdrpos(SafeFitsFile fptr, out int keynum, out int keypos, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgrec")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial int ffgrec(SafeFitsFile fptr, int keyNumber, byte* card, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffprec", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_record(SafeFitsFile fptr, string card, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffdkey", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_delete_key(SafeFitsFile fptr, string keyword, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffpkys", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_key_str(SafeFitsFile fptr, string keyword, string value, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffukys", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_update_key_str(SafeFitsFile fptr, string keyword, string value, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffpkyj", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_key_lng(SafeFitsFile fptr, string keyword, int value, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffukyj", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_update_key_lng(SafeFitsFile fptr, string keyword, int value, string comment, ref int status);

    // NOTE: CFITSIO does not have native 64-bit integer keyword functions (ffpkyjj/ffukyjj do not exist)
    // Use string-based keyword storage for 64-bit values or accept 32-bit truncation with ffpkyj
    // [LibraryImport(NativeLibraryName, EntryPoint = "ffpkyj", StringMarshalling = StringMarshalling.Utf8)]
    // [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    // public static partial int fits_write_key_lnglng(SafeFitsFile fptr, string keyword, long value, string comment, ref int status);

    // [LibraryImport(NativeLibraryName, EntryPoint = "ffukyj", StringMarshalling = StringMarshalling.Utf8)]
    // [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    // public static partial int fits_update_key_lnglng(SafeFitsFile fptr, string keyword, long value, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffpkyd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_key_dbl(SafeFitsFile fptr, string keyword, double value, int decimals, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffukyd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_update_key_dbl(SafeFitsFile fptr, string keyword, double value, int decimals, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffpkyl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_key_log(SafeFitsFile fptr, string keyword, int logicalValue, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffukyl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_update_key_log(SafeFitsFile fptr, string keyword, int logicalValue, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffpdat")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_date(SafeFitsFile fptr, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgkyj", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_read_key_lng(SafeFitsFile fptr, string keyword, ref int value, IntPtr comment, ref int status);

    // NOTE: CFITSIO has ffgkyjj for reading 64-bit, but for consistency with string-based storage
    // for write operations, we use string-based read as well
    // [LibraryImport(NativeLibraryName, EntryPoint = "ffgkyjj", StringMarshalling = StringMarshalling.Utf8)]
    // [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    // public static partial int fits_read_key_lnglng(SafeFitsFile fptr, string keyword, ref long value, IntPtr comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgkyd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_read_key_dbl(SafeFitsFile fptr, string keyword, ref double value, IntPtr comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgkyl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_read_key_log(SafeFitsFile fptr, string keyword, ref int logicalValue, IntPtr comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgkys", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial int ffgkys(SafeFitsFile fptr, string keyword, byte* value, byte* comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgkyn")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial int ffgkyn(SafeFitsFile fptr, int keyNumber, byte* keyword, byte* card, ref int status);

    // ───────────── Tables (subset) ─────────────
    [LibraryImport(NativeLibraryName, EntryPoint = "ffcrtb", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_create_tbl(SafeFitsFile fptr, int tableType, long nRows, int nFields, IntPtr ttype, IntPtr tform, IntPtr tunit, string? extname, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fficol", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_insert_col(SafeFitsFile fptr, int columnNumber, string columnName, string columnFormat, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffpcl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_col(SafeFitsFile fptr, int dataType, int columnNumber, long firstRow, long firstElem, long nElem, IntPtr values, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgcv")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_read_col(SafeFitsFile fptr, int dataType, int columnNumber, long firstRow, long firstElem, long nElem, IntPtr nullVal, IntPtr values, out int anyNull, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffdhdu")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_delete_hdu(SafeFitsFile fptr, ref int hduType, ref int status);

    // ───────────── Compression (naming varies) ─────────────
    // Most builds export these long names (no ff*). Keep as-is:
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_img_compress")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_img_compress(SafeFitsFile inFptr, SafeFitsFile outFptr, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_set_compression_type")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_set_compression_type(SafeFitsFile fptr, int ctype, ref int status);

    // NOTE: fits_set_tile_dimll and fits_set_compression_param are not exported in many CFITSIO builds.
    // Users should configure compression via fits_set_compression_type and other available APIs.
    // Uncomment and map to correct symbols if your build provides them.

    // [LibraryImport(NativeLibraryName, EntryPoint = "fits_set_tile_dimll")]
    // [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    // public static partial int fits_set_tile_dimll(SafeFitsFile fptr, int naxis, long[] tileDim, ref int status);

    // [LibraryImport(NativeLibraryName, EntryPoint = "fits_set_compression_param")]
    // [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    // public static partial int fits_set_compression_param(SafeFitsFile fptr, int nParams, float[] parameters, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_is_compressed_image")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_is_compressed_image(SafeFitsFile fptr, out int isCompressed, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffpcks")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_write_chksum(SafeFitsFile fptr, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffvcks")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_verify_chksum(SafeFitsFile fptr, out int dataOk, out int hduOk, ref int status);

    // ───────────── Error text (short symbols) ─────────────
    [LibraryImport(NativeLibraryName, EntryPoint = "ffgerr")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial void ffgerr(int status, byte* errText);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffgmsg")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial int ffgmsg(byte* errMsg);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffrprt")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void fits_report_error(IntPtr file, ref int status);

    // ───────────── Utilities ─────────────
    public static unsafe string ReadRecordToString(SafeFitsFile fptr, int keyNumber, ref int status)
    {
      byte* buf = stackalloc byte[FLEN_CARD];
      _ = ffgrec(fptr, keyNumber, buf, ref status);
      ThrowIfError(status);
      return Utf8ZToString(buf);
    }

    public static unsafe (string Keyword, string Card) ReadKeynToStrings(SafeFitsFile fptr, int keyNumber, ref int status)
    {
      byte* k = stackalloc byte[FLEN_KEYWORD];
      byte* c = stackalloc byte[FLEN_CARD];
      _ = ffgkyn(fptr, keyNumber, k, c, ref status);
      ThrowIfError(status);
      return (Utf8ZToString(k), Utf8ZToString(c));
    }

    public static unsafe string? TryReadKeyString(SafeFitsFile fptr, string keyword)
    {
      int status = 0;
      byte* v = stackalloc byte[FLEN_VALUE];
      byte* cm = stackalloc byte[FLEN_COMMENT];
      _ = ffgkys(fptr, keyword, v, cm, ref status);
      if (status != 0) return null;
      return Utf8ZToString(v);
    }

    private static unsafe string Utf8ZToString(byte* p)
    {
      if (p == null) return string.Empty;
      int len = 0; while (p[len] != 0) len++;
      return Encoding.UTF8.GetString(p, len).TrimEnd();
    }
  }
}
