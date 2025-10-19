namespace HVO.SkyMonitorV5.RPi.Models.Optics;

public sealed class OpticsCatalogOptics
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ProjectionModel { get; set; } = string.Empty;
    public double FocalLengthMillimeters { get; set; }
    public double FieldOfViewXDegrees { get; set; }
    public double? FieldOfViewYDegrees { get; set; }
}
