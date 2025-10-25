using System;
using System.IO;
using System.Runtime.InteropServices;
using HVO.Astronomy.CFITSIO.Interop;
using static HVO.Astronomy.CFITSIO.Interop.CFitsIO;

namespace HVO.Astronomy.CFITSIO
{
  public sealed partial class FitsFile
  {
    private enum MemoryBackingMode { None, InMemoryOwnedByCFitsio, InMemoryExternalBuffer }

    private IntPtr _memBufPtrLoc;
    private IntPtr _memSizeLoc;
    private MemoryBackingMode _memMode;
    private IntPtr _originalExternalBuffer;

    public bool IsInMemory => _memMode != MemoryBackingMode.None;

    private unsafe void* CurrentBufferPtr
      => _memBufPtrLoc == IntPtr.Zero ? null : *(void**)((void*)_memBufPtrLoc);

    private unsafe nuint CurrentBufferSize
      => _memSizeLoc == IntPtr.Zero ? 0 : *(nuint*)((void*)_memSizeLoc);

    public static unsafe FitsFile CreateInMemory(nuint initialCapacityBytes = 0, nuint growDeltaBytes = 64 * 1024)
    {
      int status = 0;
      // Allocate unmanaged storage for the control blocks that CFITSIO will update
      void* bufLoc = (void*)Marshal.AllocHGlobal(IntPtr.Size);
      void* sizeLoc = (void*)Marshal.AllocHGlobal(sizeof(nuint));

      try
      {
        // Initialize the control blocks
        *(void**)bufLoc = null;
        *(nuint*)sizeLoc = initialCapacityBytes;

        // Call CFITSIO - it will store these addresses and update the values over time
        CFitsIO.fits_create_memfile(out var handle, (void**)bufLoc, (nuint*)sizeLoc,
                                    growDeltaBytes, IntPtr.Zero, ref status);
        CFitsIO.ThrowIfError(status);

        return new FitsFile(handle, null)
        {
          _memBufPtrLoc = (IntPtr)bufLoc,
          _memSizeLoc = (IntPtr)sizeLoc,
          _memMode = MemoryBackingMode.InMemoryOwnedByCFitsio,
          _originalExternalBuffer = IntPtr.Zero
        };
      }
      catch
      {
        Marshal.FreeHGlobal((IntPtr)bufLoc);
        Marshal.FreeHGlobal((IntPtr)sizeLoc);
        throw;
      }
    }

    public static unsafe FitsFile OpenFromMemory(ReadOnlySpan<byte> source, bool readWrite = false, nuint growDeltaBytes = 64 * 1024)
    {
      if (source.IsEmpty) throw new ArgumentException("Empty FITS buffer.", nameof(source));

      IntPtr original = Marshal.AllocHGlobal(source.Length);
      IntPtr bufLoc = IntPtr.Zero;
      IntPtr sizeLoc = IntPtr.Zero;

      try
      {
        fixed (byte* p = source)
        {
          Buffer.MemoryCopy(p, (void*)original, source.Length, source.Length);
        }

        bufLoc = Marshal.AllocHGlobal(IntPtr.Size);
        sizeLoc = Marshal.AllocHGlobal(sizeof(nuint));
        *(void**)((void*)bufLoc) = (void*)original;
        *(nuint*)((void*)sizeLoc) = (nuint)source.Length;

        int status = 0;
        CFitsIO.fits_open_memfile(out var handle, "inmem.fits",
                                  readWrite ? CFitsIO.READWRITE : CFitsIO.READONLY,
                                  (void**)((void*)bufLoc), (nuint*)((void*)sizeLoc),
                                  readWrite ? growDeltaBytes : 0,
                                  IntPtr.Zero, ref status);
        CFitsIO.ThrowIfError(status);

        var f = new FitsFile(handle, null)
        {
          _memBufPtrLoc = bufLoc,
          _memSizeLoc = sizeLoc,
          _memMode = readWrite ? MemoryBackingMode.InMemoryOwnedByCFitsio
                               : MemoryBackingMode.InMemoryExternalBuffer,
          _originalExternalBuffer = readWrite ? IntPtr.Zero : original
        };

        if (readWrite) original = IntPtr.Zero;
        return f;
      }
      catch
      {
        if (bufLoc != IntPtr.Zero) Marshal.FreeHGlobal(bufLoc);
        if (sizeLoc != IntPtr.Zero) Marshal.FreeHGlobal(sizeLoc);
        if (original != IntPtr.Zero) Marshal.FreeHGlobal(original);
        throw;
      }
    }

    public unsafe byte[] ToArray()
    {
      if (!IsInMemory) throw new InvalidOperationException("Not an in-memory FITS.");

      int status = 0;
      CFitsIO.fits_flush_file(Handle, ref status);
      CFitsIO.ThrowIfError(status);

      var size = CurrentBufferSize;
      if (size == 0) return Array.Empty<byte>();

      var result = new byte[checked((int)size)];
      Marshal.Copy((IntPtr)CurrentBufferPtr, result, 0, result.Length);
      return result;
    }

    public void SaveToStream(Stream output)
    {
      if (output is null) throw new ArgumentNullException(nameof(output));
      var bytes = ToArray();
      output.Write(bytes, 0, bytes.Length);
    }

    private void DisposeMemoryResources()
    {
      if (_memBufPtrLoc != IntPtr.Zero)
      {
        Marshal.FreeHGlobal(_memBufPtrLoc);
        _memBufPtrLoc = IntPtr.Zero;
      }

      if (_memSizeLoc != IntPtr.Zero)
      {
        Marshal.FreeHGlobal(_memSizeLoc);
        _memSizeLoc = IntPtr.Zero;
      }

      if (_memMode == MemoryBackingMode.InMemoryExternalBuffer && _originalExternalBuffer != IntPtr.Zero)
      {
        Marshal.FreeHGlobal(_originalExternalBuffer);
        _originalExternalBuffer = IntPtr.Zero;
      }

      _memMode = MemoryBackingMode.None;
    }

    /// <summary>
    /// Compresses the current FITS file (from disk or memory) into a new compressed in-memory FITS and returns it as a byte array.
    /// This uses CFITSIO's image compression on all image HDUs, writing to an in-memory buffer.
    /// </summary>
    /// <returns>Byte array containing the compressed FITS file.</returns>
    public byte[] CompressToArray()
    {
      // Create a new in-memory FITS to hold compressed output
      using var compressed = CreateInMemory();

      // Use CFITSIO's image compression to copy from this file to the in-memory file
      // This compresses all image HDUs using CFITSIO's built-in compression
      int status = 0;
      CFitsIO.fits_img_compress(Handle, compressed.Handle, ref status);
      if (status != 0)
      {
        throw new InvalidOperationException($"Failed to compress FITS file to memory: status={status}");
      }

      // Return the compressed in-memory FITS as a byte array
      return compressed.ToArray();
    }
  }
}
