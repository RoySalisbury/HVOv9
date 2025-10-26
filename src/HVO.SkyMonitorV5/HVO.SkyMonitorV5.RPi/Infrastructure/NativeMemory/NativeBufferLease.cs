#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HVO.SkyMonitorV5.RPi.Infrastructure.NativeMemory;

/// <summary>
/// Represents a reference-counted lease over unmanaged memory. Call <see cref="Release"/> to
/// decrement the reference count or <see cref="Dispose"/> for convenience when a single release is required.
/// </summary>
public interface INativeBufferLease : IDisposable
{
    IntPtr Pointer { get; }
    long Length { get; }
    bool IsAllocated { get; }
    void AddRef();
    void Release();
}

/// <summary>
/// Factory abstraction for creating native buffer leases. Enables swapping in .NET 10 native pools later.
/// </summary>
public interface INativeBufferLeaseFactory
{
    INativeBufferLease Rent(long length);
}

/// <summary>
/// Default factory that allocates unmanaged memory via <see cref="Marshal.AllocHGlobal"/>.
/// </summary>
public sealed class HGlobalNativeBufferLeaseFactory : INativeBufferLeaseFactory
{
    public static HGlobalNativeBufferLeaseFactory Shared { get; } = new();

    private HGlobalNativeBufferLeaseFactory()
    {
    }

    public INativeBufferLease Rent(long length) => new HGlobalNativeBufferLease(length);

    private sealed class HGlobalNativeBufferLease : INativeBufferLease
    {
        private IntPtr _pointer;
        private readonly long _length;
        private int _refCount;

        public HGlobalNativeBufferLease(long length)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            _length = length;
            _pointer = Marshal.AllocHGlobal(new IntPtr(length));
            _refCount = 1;
        }

        public IntPtr Pointer => _pointer;

        public long Length => _length;

        public bool IsAllocated => _pointer != IntPtr.Zero;

        public void AddRef()
        {
            if (_pointer == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(HGlobalNativeBufferLease));
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                var ptr = Interlocked.Exchange(ref _pointer, IntPtr.Zero);
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }

        public void Dispose()
        {
            Release();
        }
    }
}

/// <summary>
/// Skia interop helpers for releasing native buffer leases.
/// </summary>
public static class NativeBufferLeaseSkiaHelpers
{
    public static void ReleasePixels(IntPtr _, object context)
    {
        if (context is INativeBufferLease lease)
        {
            lease.Release();
        }
    }
}

// TODO(dotnet10): replace HGlobal allocator with native memory pool APIs once exposed in .NET 10.
