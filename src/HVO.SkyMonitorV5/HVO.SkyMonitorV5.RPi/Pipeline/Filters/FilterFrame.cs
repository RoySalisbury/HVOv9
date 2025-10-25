#nullable enable

using System;
using HVO.SkyMonitorV5.RPi.Skia;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

/// <summary>
/// Provides a pooled, mutable surface for filters to render into during the pipeline.
/// Maintains ownership of the underlying <see cref="SkiaSurfaceLease"/> so filters
/// receive a consistent surface representation without needing to manage pooling directly.
/// </summary>
public sealed class FilterFrame : IDisposable
{
    private readonly SkiaSurfaceLease _surfaceLease;
    private bool _disposed;

    internal FilterFrame(SkiaSurfaceLease surfaceLease)
    {
        _surfaceLease = surfaceLease ?? throw new ArgumentNullException(nameof(surfaceLease));
    }

    /// <summary>Gets the mutable surface backing the current filter frame.</summary>
    public SKSurface Surface => _surfaceLease.Surface;

    /// <summary>
    /// Creates a snapshot of the current surface contents as an immutable raster <see cref="SKImage"/>.
    /// Callers assume ownership of the returned image and must dispose it when finished.
    /// </summary>
    public SKImage SnapshotImage()
    {
        EnsureNotDisposed();
        Surface.Canvas.Flush();
        using var snapshot = Surface.Snapshot() ?? throw new InvalidOperationException("Failed to snapshot filter surface.");
        var clone = SkiaImageUtilities.CloneToRaster(snapshot)
            ?? throw new InvalidOperationException("Failed to clone filter surface snapshot.");
        return clone;
    }

    /// <summary>
    /// Creates a writable <see cref="SKBitmap"/> view of the current surface using the specified colour type.
    /// Callers own the returned bitmap and must dispose it after use.
    /// </summary>
    public SKBitmap CreateBitmapView(SKColorType colorType = SKColorType.Bgra8888, SKAlphaType alphaType = SKAlphaType.Premul)
    {
        EnsureNotDisposed();
        Surface.Canvas.Flush();
        using var snapshot = Surface.Snapshot() ?? throw new InvalidOperationException("Failed to snapshot filter surface.");
        return SkiaImageUtilities.CreateBitmapCopy(snapshot, colorType, alphaType);
    }

    /// <summary>
    /// Updates the surface with the contents of the provided bitmap. The bitmap is not disposed.
    /// </summary>
    public void BlitBitmap(SKBitmap bitmap)
    {
        EnsureNotDisposed();
        if (bitmap is null)
        {
            throw new ArgumentNullException(nameof(bitmap));
        }

        Surface.Canvas.DrawBitmap(bitmap, 0, 0);
        Surface.Canvas.Flush();
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FilterFrame));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _surfaceLease.Dispose();
    }
}
