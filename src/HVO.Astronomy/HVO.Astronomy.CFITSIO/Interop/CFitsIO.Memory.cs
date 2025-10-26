using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HVO.Astronomy.CFITSIO.Interop
{
  public static partial class CFitsIO
  {
    /// <summary>
    /// Create a new FITS file entirely in memory (CFITSIO owns/grows the buffer).
    /// </summary>
    /// <remarks>
    /// IMPORTANT: CFITSIO keeps the addresses of bufferPtrLocation and bufferSizeLocation
    /// and writes to them over the lifetime of the file handle. These must point to
    /// unmanaged storage that remains valid until the file is closed.
    /// C signature: int ffimem(fitsfile **fptr, void **buffptr, size_t *buffsize, size_t deltasize, void *(*mem_realloc)(...), int *status)
    /// </remarks>
    [DllImport(NativeLibraryName, EntryPoint = "ffimem", CallingConvention = CallingConvention.Cdecl)]
    public static unsafe extern int fits_create_memfile(
      out SafeFitsFile fitsFile,
      void** bufferPtrLocation,
      nuint* bufferSizeLocation,
      nuint deltaSize,
      IntPtr memoryReallocCallback,
      ref int status);

    /// <summary>
    /// Open an existing FITS byte buffer as an in-memory file.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: CFITSIO keeps the addresses of bufferPtrLocation and bufferSizeLocation
    /// and writes to them over the lifetime of the file handle. These must point to
    /// unmanaged storage that remains valid until the file is closed.
    /// C signature: int ffomem(fitsfile **fptr, const char *name, int mode, void **buffptr, size_t *buffsize, size_t deltasize, void *(*mem_realloc)(...), int *status)
    /// </remarks>
    [DllImport(NativeLibraryName, EntryPoint = "ffomem", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static unsafe extern int fits_open_memfile(
      out SafeFitsFile fitsFile,
      string fileName,
      int ioMode,
      void** bufferPtrLocation,
      nuint* bufferSizeLocation,
      nuint deltaSize,
      IntPtr memoryReallocCallback,
      ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffflus")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_flush_file(SafeFitsFile fitsFile, ref int status);

    [LibraryImport(NativeLibraryName, EntryPoint = "ffflsh")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int fits_flush_buffer(SafeFitsFile fitsFile, int clear, ref int status);
  }
}
