#nullable enable

using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Per-frame rendering context shared across filters so overlays use the exact same
/// projection/lens geometry as the main render step.
/// </summary>
public sealed record FrameRenderContext(
    StarFieldEngine Engine,
    IImageProjector Projector,
    DateTime Utc,
    double LatitudeDeg,
    double LongitudeDeg,
    bool FlipHorizontal);