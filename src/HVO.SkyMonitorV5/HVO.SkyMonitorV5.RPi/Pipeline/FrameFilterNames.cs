#nullable enable

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Provides shared names for frame filters so configuration can reference them without relying on concrete types.
/// </summary>
public static class FrameFilterNames
{
    public const string CardinalDirections = "CardinalDirections";
    public const string ConstellationFigures = "ConstellationFigures";
    public const string CelestialAnnotations = "CelestialAnnotations";
    public const string OverlayText = "OverlayText";
    public const string CircularApertureMask = "CircularApertureMask";
    public const string DiagnosticsOverlay = "DiagnosticsOverlay";
}
