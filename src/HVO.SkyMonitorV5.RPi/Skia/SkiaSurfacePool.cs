using System;
using System.Collections.Generic;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Skia;

/// <summary>
/// Provides pooled access to linear <see cref="SKSurface"/> instances to reduce allocation churn
/// during high-frequency stacking and preprocessing operations.
/// </summary>
public sealed class SkiaSurfacePool : IDisposable
{
    private static readonly SKColorSpace LinearSrgb = SKColorSpace.CreateSrgbLinear();

    private readonly object _syncRoot = new();
    private readonly Dictionary<(int Width, int Height), Stack<SKSurface>> _surfaces = new();
    private bool _disposed;

    public SkiaSurfaceLease RentLinearSurface(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException("Surface dimensions must be positive.");
        }

        lock (_syncRoot)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SkiaSurfacePool));
            }

            if (_surfaces.TryGetValue((width, height), out var stack) && stack.Count > 0)
            {
                var pooled = stack.Pop();
                pooled.Canvas.Clear(SKColors.Transparent);
                return new SkiaSurfaceLease(this, pooled, width, height);
            }
        }

        var info = new SKImageInfo(width, height, SKColorType.RgbaF16, SKAlphaType.Premul, LinearSrgb);
        var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"Failed to allocate SKSurface {width}x{height}.");

        return new SkiaSurfaceLease(this, surface, width, height);
    }

    internal void Return(SKSurface surface, (int Width, int Height) key)
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                surface.Dispose();
                return;
            }

            if (!_surfaces.TryGetValue(key, out var stack))
            {
                stack = new Stack<SKSurface>();
                _surfaces[key] = stack;
            }

            surface.Canvas.Clear(SKColors.Transparent);
            stack.Push(surface);
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var stack in _surfaces.Values)
            {
                while (stack.Count > 0)
                {
                    stack.Pop().Dispose();
                }
            }

            _surfaces.Clear();
            _disposed = true;
        }
    }
}

public sealed class SkiaSurfaceLease : IDisposable
{
    private SkiaSurfacePool? _owner;
    private readonly (int Width, int Height) _key;

    internal SkiaSurfaceLease(SkiaSurfacePool owner, SKSurface surface, int width, int height)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _key = (width, height);
    }

    public SKSurface Surface { get; }

    public void Dispose()
    {
        var owner = _owner;
        if (owner is null)
        {
            return;
        }

        _owner = null;
        owner.Return(Surface, _key);
    }
}
