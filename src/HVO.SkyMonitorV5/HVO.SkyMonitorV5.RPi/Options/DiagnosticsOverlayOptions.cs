using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration for the diagnostics overlay filter. Displays capture and rig telemetry for debugging overlays.
/// </summary>
public sealed class DiagnosticsOverlayOptions
{
    public const string SectionName = "CameraPipeline:DiagnosticsOverlay";

    /// <summary>Enables or disables the diagnostics overlay filter.</summary>
    public bool Enabled { get; set; }

    /// <summary>Controls which corner of the frame renders the diagnostics block.</summary>
    [EnumDataType(typeof(OverlayCorner))]
    public OverlayCorner Corner { get; set; } = OverlayCorner.TopRight;

    [Range(8f, 48f)]
    public float TitleFontSize { get; set; } = 16f;

    [Range(6f, 36f)]
    public float BodyFontSize { get; set; } = 14f;

    [Range(0f, 96f)]
    public float Margin { get; set; } = 18f;

    [Range(0f, 32f)]
    public float LineSpacing { get; set; } = 4f;

    public bool ShowRigDetails { get; set; } = true;

    public bool ShowProjectorDetails { get; set; } = true;

    public bool ShowStackingMetrics { get; set; } = true;

    public bool ShowContextFlags { get; set; } = true;
}

/// <summary>Placement options for overlay blocks.</summary>
public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}
