#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using HVO.SkyMonitorV5.RPi.Models;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Composition;

/// <summary>
/// Small ring buffer that retains the most recent composed frames and associated metadata,
/// ensuring disposed images are released promptly when overwritten.
/// </summary>
public sealed class ComposedFrameQueue : IDisposable
{
    private readonly object _sync = new();
    private readonly int _capacity;
    private readonly FrameSlot[] _slots;
    private int _writeIndex;
    private bool _disposed;

    public ComposedFrameQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Queue capacity must be positive.");
        }

        _capacity = capacity;
        _slots = new FrameSlot[capacity];
    }

    public int Capacity => _capacity;

    public void Enqueue(ComposedFrame frame)
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            ref var slot = ref _slots[_writeIndex];
            slot.Dispose();
            slot = new FrameSlot(frame);

            _writeIndex++;
            if (_writeIndex >= _capacity)
            {
                _writeIndex = 0;
            }
        }
    }

    public IReadOnlyList<ComposedFrame> Snapshot()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            var results = new List<ComposedFrame>(_capacity);
            for (var i = 0; i < _capacity; i++)
            {
                var index = (_writeIndex - 1 - i + _capacity) % _capacity;
                var slot = _slots[index];
                if (slot.Frame is null)
                {
                    break;
                }

                results.Add(slot.Frame.Value);
            }

            return results;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            for (var i = 0; i < _slots.Length; i++)
            {
                _slots[i].Dispose();
            }

            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ComposedFrameQueue));
        }
    }

    private readonly struct FrameSlot : IDisposable
    {
        public FrameSlot(ComposedFrame frame)
        {
            Frame = frame;
        }

        public ComposedFrame? Frame { get; }

        public void Dispose()
        {
            if (Frame is { } value)
            {
                value.Image.Dispose();
            }
        }
    }
}

public readonly record struct ComposedFrame(
    Guid FrameId,
    DateTimeOffset Timestamp,
    SKImage Image,
    int FramesStacked,
    int IntegrationMilliseconds,
    IReadOnlyList<string> AppliedFilters,
    IReadOnlyList<FilterExecution> FilterExecutions,
    double SurfaceMilliseconds);