#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Overlays;

/// <summary>
/// Provides a shared cache for reusable overlay assets such as <see cref="SKPicture"/> and <see cref="SKImage"/> instances.
/// Ensures assets are disposed when invalidated and allows filters to reuse expensive recordings across frames.
/// </summary>
public sealed class OverlayAssetCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<CachedPicture>> _pictureCache = new();
    private readonly ConcurrentDictionary<string, Lazy<CachedImage>> _imageCache = new();
    private readonly object _disposeSync = new();
    private bool _disposed;

    public SKPicture GetOrCreatePicture(string key, Func<SKPicture> factory)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        ThrowIfDisposed();

        var lazy = _pictureCache.GetOrAdd(key, static (k, f) =>
            new Lazy<CachedPicture>(() => new CachedPicture(f()), LazyThreadSafetyMode.ExecutionAndPublication), factory);

        var cached = lazy.Value;
        return cached.Picture;
    }

    public SKImage GetOrCreateImage(string key, Func<SKImage> factory)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        ThrowIfDisposed();

        var lazy = _imageCache.GetOrAdd(key, static (k, f) =>
            new Lazy<CachedImage>(() => new CachedImage(f()), LazyThreadSafetyMode.ExecutionAndPublication), factory);

        var cached = lazy.Value;
        return cached.Image;
    }

    public void InvalidateGroup(string groupPrefix)
    {
        if (string.IsNullOrWhiteSpace(groupPrefix))
        {
            return;
        }

        ThrowIfDisposed();

        foreach (var key in _pictureCache.Keys)
        {
            if (!key.StartsWith(groupPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (_pictureCache.TryRemove(key, out var lazy) && lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }

        foreach (var key in _imageCache.Keys)
        {
            if (!key.StartsWith(groupPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (_imageCache.TryRemove(key, out var lazy) && lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        DisposePictures(_pictureCache.Values);
        _pictureCache.Clear();
        DisposeImages(_imageCache.Values);
        _imageCache.Clear();
    }

    private void DisposePictures(IEnumerable<Lazy<CachedPicture>> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsValueCreated)
            {
                entry.Value.Dispose();
            }
        }
    }

    private void DisposeImages(IEnumerable<Lazy<CachedImage>> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsValueCreated)
            {
                entry.Value.Dispose();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OverlayAssetCache));
        }
    }

    public void Dispose()
    {
        lock (_disposeSync)
        {
            if (_disposed)
            {
                return;
            }

            DisposePictures(_pictureCache.Values);
            _pictureCache.Clear();
            DisposeImages(_imageCache.Values);
            _imageCache.Clear();
            _disposed = true;
        }
    }

    private sealed class CachedPicture : IDisposable
    {
        public CachedPicture(SKPicture picture)
        {
            Picture = picture ?? throw new ArgumentNullException(nameof(picture));
        }

        public SKPicture Picture { get; }

        public void Dispose()
        {
            Picture.Dispose();
        }
    }

    private sealed class CachedImage : IDisposable
    {
        public CachedImage(SKImage image)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
        }

        public SKImage Image { get; }

        public void Dispose()
        {
            Image.Dispose();
        }
    }
}
