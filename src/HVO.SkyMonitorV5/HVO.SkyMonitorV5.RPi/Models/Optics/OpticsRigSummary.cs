namespace HVO.SkyMonitorV5.RPi.Models.Optics;

public sealed class OpticsRigSummary
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CameraKey { get; set; } = string.Empty;
    public string CameraDisplayName { get; set; } = string.Empty;
    public string OpticsKey { get; set; } = string.Empty;
    public string OpticsDisplayName { get; set; } = string.Empty;
    public double BoresightAltitudeDegrees { get; set; }
    public double BoresightAzimuthDegrees { get; set; }
    public bool IsActive { get; set; }
    public bool HasAdapterBindings { get; set; }
    public long Revision { get; set; }
}
