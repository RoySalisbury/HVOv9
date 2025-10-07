using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Represents the outcome of stacking operations, including metadata for downstream processing.
/// </summary>
public sealed record FrameStackResult(CameraFrame Frame, int FramesCombined, int IntegrationMilliseconds);
