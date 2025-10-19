namespace HVO.SkyMonitorV5.Data.Configurations.Entities;

/// <summary>
/// Represents a lens specification stored in the SkyMonitor catalog.
/// </summary>
public sealed class CameraCatalogLensEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProjectionModel { get; set; } = string.Empty;
    public double FocalLengthMillimeters { get; set; }
    public double FieldOfViewXDegrees { get; set; }
    public double? FieldOfViewYDegrees { get; set; }
    public double RollDegrees { get; set; }
    public string Kind { get; set; } = string.Empty;
}
