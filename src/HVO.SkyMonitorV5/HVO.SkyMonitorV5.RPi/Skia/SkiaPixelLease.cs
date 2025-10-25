using System;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Skia;

/// <summary>
/// Represents a scoped lease over Skia pixel memory, exposing an <see cref="SKPixmap"/>
/// view while managing ownership of the underlying buffer.
/// </summary>
public sealed class SkiaPixelLease : IDisposable
{
    private readonly Action? _onDispose;
    private bool _disposed;

    private SkiaPixelLease(SKPixmap pixmap, Action? onDispose)
    {
        Pixmap = pixmap ?? throw new ArgumentNullException(nameof(pixmap));
        _onDispose = onDispose;
    }

    /// <summary>
    /// Gets the pixmap view over the leased pixel buffer.
    /// </summary>
    public SKPixmap Pixmap { get; }

    /// <summary>
    /// Creates a lease that wraps the provided bitmap without copying pixels.
    /// </summary>
    /// <remarks>
    /// The resulting lease takes ownership of the bitmap when <paramref name="disposeBitmap"/> is true.
    /// The caller should not dispose the bitmap independently while the lease is active.
    /// </remarks>
    public static SkiaPixelLease FromBitmap(SKBitmap bitmap, bool disposeBitmap = true)
    {
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        var pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            throw new InvalidOperationException("Bitmap does not expose pixel memory for leasing.");
        }

        var pixmap = new SKPixmap(bitmap.Info, pixels, bitmap.RowBytes);
        Action? disposeOwner = disposeBitmap ? bitmap.Dispose : null;
        return new SkiaPixelLease(pixmap, disposeOwner);
    }

    /// <summary>
    /// Creates a lease from an unmanaged pixel buffer.
    /// </summary>
    /// <param name="info">Image description for the pixel buffer.</param>
    /// <param name="pixels">Pointer to the pixel buffer.</param>
    /// <param name="rowBytes">Stride in bytes.</param>
    /// <param name="onDispose">Optional callback invoked when the lease is disposed.</param>
    public static SkiaPixelLease FromPixels(SKImageInfo info, IntPtr pixels, int rowBytes, Action? onDispose = null)
    {
        if (pixels == IntPtr.Zero)
        {
            throw new ArgumentException("Pixel pointer must not be zero.", nameof(pixels));
        }

        var pixmap = new SKPixmap(info, pixels, rowBytes);
        return new SkiaPixelLease(pixmap, onDispose);
    }

    /// <summary>
    /// Creates an immutable image snapshot referencing the leased pixels when possible.
    /// Consumers must dispose the returned image when finished.
    /// </summary>
    public SKImage Snapshot(bool copyPixels = false)
    {
        ThrowIfDisposed();

        var image = SKImage.FromPixels(Pixmap)
            ?? throw new InvalidOperationException("Failed to snapshot pixmap.");

        if (!copyPixels)
        {
            return image;
        }

        try
        {
            var raster = SkiaImageUtilities.CloneToRaster(image)
                ?? throw new InvalidOperationException("Failed to clone pixmap image.");

            return raster;
        }
        finally
        {
            image.Dispose();
        }
    }

    public Span<byte> GetPixelSpan()
    {
        ThrowIfDisposed();
        return Pixmap.GetPixelSpan();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Pixmap.Dispose();
        _onDispose?.Invoke();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SkiaPixelLease));
        }
    }
}
