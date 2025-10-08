#nullable enable

using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Captures the per-frame rig, projection, and rendering state produced by a camera adapter.
/// </summary>
public sealed record FrameContext(
    RigSpec Rig,
    IImageProjector Projector,
    StarFieldEngine Engine,
    DateTimeOffset Timestamp,
    double LatitudeDeg,
    double LongitudeDeg,
    bool FlipHorizontal,
    double? HorizonPadding = null,
    bool ApplyRefraction = false,
    Action<FrameContext>? DisposeAction = null) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            DisposeAction?.Invoke(this);
        }
        finally
        {
            (Engine as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}
