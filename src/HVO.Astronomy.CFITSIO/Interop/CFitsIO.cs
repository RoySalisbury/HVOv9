#nullable enable
using System;
using System.Runtime.CompilerServices;   // CallConvCdecl
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HVO.Astronomy.CFITSIO.Interop
{
  /// <summary>
  /// Source-generated P/Invoke surface for the native <c>CFITSIO</c> library (cdecl, UTF-8).
  /// <list type="bullet">
  ///   <item><description>Uses <see cref="LibraryImportAttribute"/> with UTF-8 marshalling.</description></item>
  ///   <item><description>All <c>fitsfile*</c> handles are represented by <see cref="SafeFitsFile"/>.</description></item>
  ///   <item><description>No <see cref="StringBuilder"/> parameters in P/Invokes; helper methods provide string-returning wrappers.</description></item>
  /// </list>
  /// <para><b>Indexing:</b> CFITSIO uses 1-based indices for HDUs, pixels, and table rows.</para>
  /// </summary>
  public static partial class CFitsIO
  {
    private const string NativeLibraryName = "cfitsio";

    // CFITSIO recommended buffer sizes (safe upper bounds)
    private const int FLEN_STATUS = 81;   // status description (fits_get_errstatus)
    private const int FLEN_ERRMSG = 81;   // one error stack line (fits_read_errmsg)
    private const int FLEN_CARD = 81;  // header card (80 chars + NUL)
    private const int FLEN_KEYWORD = 72;  // keyword name (enough for read_keyn)
    private const int FLEN_VALUE = 1024; // generous value buffer for read_key_str
    private const int FLEN_COMMENT = 1024; // generous comment buffer for read_key_str

    // ───────────────────────────── SafeHandle ─────────────────────────────

    /// <summary>
    /// Safe handle for a native <c>fitsfile*</c>. Disposing this handle closes the file via <c>fits_close_file</c>.
    /// </summary>
    public sealed class SafeFitsFile : SafeHandleZeroOrMinusOneIsInvalid
    {
      public SafeFitsFile() : base(ownsHandle: true) { }
      protected override bool ReleaseHandle()
      {
        int status = 0;
        fits_close_file(handle, ref status);
        handle = IntPtr.Zero;
        return true;
      }
    }

    // ───────────────────────────── Exceptions ─────────────────────────────

    /// <summary>Exception thrown when a CFITSIO function returns a non-zero status code.</summary>
    public sealed class FitsInteropException : InvalidOperationException
    {
      public int Status { get; }
      public FitsInteropException(int status, string message)
          : base($"CFITSIO error {status}: {message}") => Status = status;
    }

    /// <summary>
    /// Throws a <see cref="FitsInteropException"/> if <paramref name="status"/> is non-zero.
    /// Includes a one-line errstatus plus any queued error stack lines.
    /// </summary>
    public static void ThrowIfError(int status)
    {
      if (status == 0) return;

      string summary = GetErrorStatusText(status);
      string stack = DrainErrorStack();
      string message = string.IsNullOrEmpty(stack) ? summary : $"{summary} | CFITSIO stack:{Environment.NewLine}{stack}";
      throw new FitsInteropException(status, message);
    }

    /// <summary>Get the one-line description for a CFITSIO status code.</summary>
    public static unsafe string GetErrorStatusText(int status)
    {
      byte* buf = stackalloc byte[FLEN_STATUS];
      fits_get_errstatus(status, buf);
      return Utf8ZToString(buf);
    }

    /// <summary>Drain the CFITSIO error stack and return all lines as a single string separated by newlines.</summary>
    public static unsafe string DrainErrorStack()
    {
      var sb = new StringBuilder();
      byte* line = stackalloc byte[FLEN_ERRMSG];
      while (fits_read_errmsg(line) != 0)
      {
        sb.AppendLine(Utf8ZToString(line));
      }
      return sb.ToString();
    }

    // ───────────────────────────── Constants ──────────────────────────────

    public const int READONLY = 0;
    public const int READWRITE = 1;

    public const int IMAGE_HDU = 0;
    public const int ASCII_TBL = 1;
    public const int BINARY_TBL = 2;
    public const int ANY_HDU = -1;

    public const int BYTE_IMG = 8;
    public const int SHORT_IMG = 16;
    /// <summary>Pseudo-BITPIX for unsigned 16-bit (BITPIX=16 with BZERO=32768, BSCALE=1).</summary>
    public const int USHORT_IMG = 20;
    public const int LONG_IMG = 32;
    public const int LONGLONG_IMG = 64;
    public const int FLOAT_IMG = -32;
    public const int DOUBLE_IMG = -64;

    // Data type codes for (read|write)_img/col
    public const int TBIT = 1, TBYTE = 11, TSBYTE = 12, TUSHORT = 20, TSHORT = 21, TUINT = 30, TINT = 31;
    public const int TULONG = 40, TLONG = 41, TLONGLONG = 81, TFLOAT = 42, TDOUBLE = 82, TLOGICAL = 14, TSTRING = 16;

    // Compression algorithm codes
    public const int RICE_1 = 11, GZIP_1 = 21, GZIP_2 = 22, PLIO_1 = 31, HCOMPRESS_1 = 41;

    // ───────────────────────────── Files ──────────────────────────────────

    /// <summary>Open an existing FITS file.</summary>
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_open_file", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_open_file(out SafeFitsFile fitsFile, string fileName, int ioMode, ref int status);

    /// <summary>Create a new FITS file. Prefix <paramref name="fileName"/> with '!' to overwrite.</summary>
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_create_file", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_create_file(out SafeFitsFile fitsFile, string fileName, ref int status);

    /// <summary>Close a FITS file. Usually invoked by <see cref="SafeFitsFile.ReleaseHandle"/>.</summary>
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_close_file")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_close_file(IntPtr fitsFile, ref int status);

    /// <summary>Delete a FITS file on disk.</summary>
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_delete_file")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_delete_file(SafeFitsFile fitsFile, ref int status);

    /// <summary>Get the CFITSIO runtime version.</summary>
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_get_version")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_get_version(out double version);

    // ─────────────── HDU navigation / introspection ────────────────

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_movabs_hdu")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_movabs_hdu(SafeFitsFile fitsFile, int absoluteHduNumber, out int hduType, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_movrel_hdu")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_movrel_hdu(SafeFitsFile fitsFile, int relativeHduOffset, out int hduType, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_get_hdu_num")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_get_hdu_num(SafeFitsFile fitsFile, out int absoluteHduNumber);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_get_num_hdus")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_get_num_hdus(SafeFitsFile fitsFile, out int numberOfHdus, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_get_hdu_type")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_get_hdu_type(SafeFitsFile fitsFile, out int hduType, ref int status);

    // ───────────────────── Image create / query ─────────────────────

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_create_imgll")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_create_imgll(SafeFitsFile fitsFile, int bitpix, int numberOfAxes, long[] axisLengths, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_insert_imgll")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_insert_imgll(SafeFitsFile fitsFile, int bitpix, int numberOfAxes, long[] axisLengths, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_get_img_paramll")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_get_img_paramll(SafeFitsFile fitsFile, int maximumAxes, out int bitpix, out int numberOfAxes, long[] axisLengths, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_set_bscale")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_set_bscale(SafeFitsFile fitsFile, double bScale, double bZero, ref int status);

    // ───────────────────────── Image I/O ───────────────────────────

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_img")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_img(SafeFitsFile fitsFile, int dataType, long firstElementIndex, long numberOfElements, IntPtr sourceArray, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_img")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_read_img(SafeFitsFile fitsFile, int dataType, long firstElementIndex, long numberOfElements, IntPtr nullValue, IntPtr destinationArray, out int anyNull, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_subset")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_subset(SafeFitsFile fitsFile, int dataType, long[] firstPixel, long[] lastPixel, IntPtr sourceArray, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_subset")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_read_subset(SafeFitsFile fitsFile, int dataType, long[] firstPixel, long[] lastPixel, long[]? pixelStep, IntPtr nullValue, IntPtr destinationArray, out int anyNull, ref int status);

    // ───────────────────── Headers / Keywords ──────────────────────

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_get_hdrspace")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_get_hdrspace(SafeFitsFile fitsFile, out int numberOfCards, out int positionOfNextKey, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_get_hdrpos")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_get_hdrpos(SafeFitsFile fitsFile, out int currentKeyNumber, out int currentKeyPosition, ref int status);

    // read/write raw cards
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_record")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial int fits_read_record(SafeFitsFile fitsFile, int keyNumber, byte* card, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_record", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_record(SafeFitsFile fitsFile, string card, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_delete_key", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_delete_key(SafeFitsFile fitsFile, string keyword, ref int status);

    // typed key write/update
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_key_str", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_key_str(SafeFitsFile fitsFile, string keyword, string value, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_update_key_str", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_update_key_str(SafeFitsFile fitsFile, string keyword, string value, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_key_lng", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_key_lng(SafeFitsFile fitsFile, string keyword, int value, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_update_key_lng", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_update_key_lng(SafeFitsFile fitsFile, string keyword, int value, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_key_lnglng", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_key_lnglng(SafeFitsFile fitsFile, string keyword, long value, string comment, ref int status);

    // 64-bit (long long) KEYWORD update
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_update_key_lnglng", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_update_key_lnglng(
        SafeFitsFile fitsFile,
        string keyword,
        long value,
        string comment,
        ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_key_dbl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_key_dbl(SafeFitsFile fitsFile, string keyword, double value, int decimals, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_update_key_dbl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_update_key_dbl(SafeFitsFile fitsFile, string keyword, double value, int decimals, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_key_log", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_key_log(SafeFitsFile fitsFile, string keyword, int logicalValue, string comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_update_key_log", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_update_key_log(SafeFitsFile fitsFile, string keyword, int logicalValue, string comment, ref int status);

    /// <summary>Write the current UTC date to the <c>DATE</c> keyword of the current HDU.</summary>
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_date")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_date(SafeFitsFile fitsFile, ref int status);

    // typed key reads (note comment is OUT; we pass null/zero)
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_key_lng", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_read_key_lng(SafeFitsFile fitsFile, string keyword, ref int value, IntPtr comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_key_lnglng", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_read_key_lnglng(SafeFitsFile fitsFile, string keyword, ref long value, IntPtr comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_key_dbl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_read_key_dbl(SafeFitsFile fitsFile, string keyword, ref double value, IntPtr comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_key_log", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_read_key_log(SafeFitsFile fitsFile, string keyword, ref int logicalValue, IntPtr comment, ref int status);

    // string key read / key+card read (use byte* buffers)
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_key_str", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial int fits_read_key_str(SafeFitsFile fitsFile, string keyword, byte* value, byte* comment, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_keyn")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial int fits_read_keyn(SafeFitsFile fitsFile, int keyNumber, byte* keyword, byte* card, ref int status);

    // ─────────────────────────── Tables ────────────────────────────

    /// <summary>Create a table extension (use <see cref="Utf8StringArray"/> to pass string arrays).</summary>
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_create_tbl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_create_tbl(
        SafeFitsFile fitsFile,
        int tableType,
        long numberOfRows,
        int numberOfFields,
        IntPtr columnNamesUtf8Array,   // char** ttype
        IntPtr columnFormatsUtf8Array, // char** tform
        IntPtr columnUnitsUtf8Array,   // char** tunit
        string? extensionName,
        ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_insert_col", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_insert_col(SafeFitsFile fitsFile, int columnNumber, string columnName, string columnFormat, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_col")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_col(SafeFitsFile fitsFile, int dataType, int columnNumber, long firstRow, long firstElement, long numberOfElements, IntPtr values, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_col")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_read_col(SafeFitsFile fitsFile, int dataType, int columnNumber, long firstRow, long firstElement, long numberOfElements, IntPtr nullValue, IntPtr values, out int anyNull, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_delete_hdu")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_delete_hdu(SafeFitsFile fitsFile, ref int hduType, ref int status);

    // ─────────────────────── Compression ───────────────────────────

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_set_compression_type")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_set_compression_type(SafeFitsFile fitsFile, int compressionType, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_set_tile_dimll")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_set_tile_dimll(SafeFitsFile fitsFile, int numberOfAxes, long[] tileDimensions, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_set_compression_param")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_set_compression_param(SafeFitsFile fitsFile, int numberOfParameters, float[] parameters, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_is_compressed_image")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_is_compressed_image(SafeFitsFile fitsFile, out int isCompressed, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_img_compress")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_img_compress(SafeFitsFile inputFitsFile, SafeFitsFile outputFitsFile, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_copy_hdu")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_copy_hdu(SafeFitsFile inputFitsFile, SafeFitsFile outputFitsFile, int moreKeys, ref int status);

    // ───────────────────── Checksums / Errors ──────────────────────

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_write_chksum")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_write_chksum(SafeFitsFile fitsFile, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_verify_chksum")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial int fits_verify_chksum(SafeFitsFile fitsFile, out int dataIsOk, out int hduIsOk, ref int status);

    // NOTE: Use unsafe byte* buffers (no StringBuilder in source-generated interop)
    [LibraryImport(NativeLibraryName, EntryPoint = "fits_get_errstatus")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial int fits_get_errstatus(int status, byte* errorText);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_read_errmsg")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial int fits_read_errmsg(byte* errorMessage);

    [LibraryImport(NativeLibraryName, EntryPoint = "fits_report_error")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static partial void fits_report_error(IntPtr filePointer, int status);

    // ───────────────────── Pin helpers (spans) ─────────────────────

    /// <summary>Pin a span and pass an unmanaged pointer to a callback (for array arguments to native calls).</summary>
    public static unsafe void WithPinned<T>(Span<T> span, Action<IntPtr> action) where T : unmanaged
    {
      if (action is null) throw new ArgumentNullException(nameof(action));
      fixed (T* p = span) action((IntPtr)p);
    }

    /// <summary>Pin a read-only span and pass an unmanaged pointer to a callback (for array arguments to native calls).</summary>
    public static unsafe void WithPinned<T>(ReadOnlySpan<T> span, Action<IntPtr> action) where T : unmanaged
    {
      if (action is null) throw new ArgumentNullException(nameof(action));
      fixed (T* p = span) action((IntPtr)p);
    }

    // ───────────── UTF-8 string[] → char** marshalling helper ────────────

    /// <summary>
    /// Utility for marshalling managed <see cref="string"/> arrays as unmanaged UTF-8 <c>char**</c>.
    /// Use in a <c>using</c> block and pass <see cref="Pointer"/> to functions like <c>fits_create_tbl</c>.
    /// </summary>
    public sealed class Utf8StringArray : IDisposable
    {
      private readonly IntPtr _pointerArray;
      private readonly IntPtr[] _itemPointers;
      private bool _disposed;

      /// <summary>Pointer to the unmanaged <c>char**</c> array (or <see cref="IntPtr.Zero"/> if empty/null).</summary>
      public IntPtr Pointer => _pointerArray;

      private Utf8StringArray(IntPtr pointerArray, IntPtr[] itemPointers)
      {
        _pointerArray = pointerArray;
        _itemPointers = itemPointers;
      }

      /// <summary>Create an unmanaged UTF-8 <c>char**</c> from a managed <see cref="string"/> array (null → <see cref="IntPtr.Zero"/>).</summary>
      public static Utf8StringArray From(string[]? values)
      {
        if (values is null || values.Length == 0)
          return new Utf8StringArray(IntPtr.Zero, Array.Empty<IntPtr>());

        var items = new IntPtr[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
          if (values[i] is null) { items[i] = IntPtr.Zero; continue; }
          var bytes = Encoding.UTF8.GetBytes(values[i]);
          var p = Marshal.AllocHGlobal(bytes.Length + 1);
          Marshal.Copy(bytes, 0, p, bytes.Length);
          Marshal.WriteByte(p + bytes.Length, 0);
          items[i] = p;
        }

        int size = IntPtr.Size * items.Length;
        var arr = Marshal.AllocHGlobal(size);
        for (int i = 0; i < items.Length; i++)
          Marshal.WriteIntPtr(arr, i * IntPtr.Size, items[i]);

        return new Utf8StringArray(arr, items);
      }

      public void Dispose()
      {
        if (_disposed) return;
        _disposed = true;

        foreach (var p in _itemPointers)
          if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);

        if (_pointerArray != IntPtr.Zero)
          Marshal.FreeHGlobal(_pointerArray);
      }
    }

    // ─────────────── String-returning helper wrappers ───────────────

    /// <summary>Read an 80-char header card by index and return it as a managed string.</summary>
    public static unsafe string ReadRecordToString(SafeFitsFile fitsFile, int keyNumber, ref int status)
    {
      byte* buf = stackalloc byte[FLEN_CARD];
      int rc = fits_read_record(fitsFile, keyNumber, buf, ref status);
      ThrowIfError(status);
      return Utf8ZToString(buf);
    }

    /// <summary>Read keyword and full card by index. Returns (keyword, card).</summary>
    public static unsafe (string Keyword, string Card) ReadKeynToStrings(SafeFitsFile fitsFile, int keyNumber, ref int status)
    {
      byte* kbuf = stackalloc byte[FLEN_KEYWORD];
      byte* cbuf = stackalloc byte[FLEN_CARD];
      int rc = fits_read_keyn(fitsFile, keyNumber, kbuf, cbuf, ref status);
      ThrowIfError(status);
      return (Utf8ZToString(kbuf), Utf8ZToString(cbuf));
    }

    /// <summary>Read a string keyword value (and ignore the returned comment). Returns null if not found.</summary>
    public static unsafe string? TryReadKeyString(SafeFitsFile fitsFile, string keyword)
    {
      int status = 0;
      byte* vbuf = stackalloc byte[FLEN_VALUE];
      byte* cbuf = stackalloc byte[FLEN_COMMENT];
      int rc = fits_read_key_str(fitsFile, keyword, vbuf, cbuf, ref status);
      if (status != 0) return null;
      return Utf8ZToString(vbuf);
    }

    // ─────────────────────────── Utilities ──────────────────────────

    private static unsafe string Utf8ZToString(byte* p)
    {
      if (p == null) return string.Empty;
      int len = 0; while (p[len] != 0) len++;
      return Encoding.UTF8.GetString(p, len).TrimEnd(); // trim trailing spaces common in FITS cards
    }
  }
}
