#nullable enable

using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Per-frame rendering context shared across filters so overlays use the exact same
/// projection/lens geometry as the main render step.
/// </summary>
public sealed record FrameRenderContext(FrameContext FrameContext)
{
    public RigSpec Rig => FrameContext.Rig;
    public IImageProjector Projector => FrameContext.Projector;
    public StarFieldEngine Engine => FrameContext.Engine;
    public DateTimeOffset Timestamp => FrameContext.Timestamp;
    public double LatitudeDeg => FrameContext.LatitudeDeg;
    public double LongitudeDeg => FrameContext.LongitudeDeg;
    public bool FlipHorizontal => FrameContext.FlipHorizontal;
    public double? HorizonPadding => FrameContext.HorizonPadding;
    public bool ApplyRefraction => FrameContext.ApplyRefraction;
}