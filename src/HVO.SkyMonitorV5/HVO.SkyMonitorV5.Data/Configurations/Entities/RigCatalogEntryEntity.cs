namespace HVO.SkyMonitorV5.Data.Configurations.Entities;

/// <summary>
/// Represents an all-sky rig definition that links camera and lens catalog entries.
/// </summary>
public sealed class RigCatalogEntryEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int CameraId { get; set; }
    public CameraCatalogEntity? Camera { get; set; }
    public int LensId { get; set; }
    public OpticsCatalogEntity? Lens { get; set; }
    public double BoresightAltitudeDegrees { get; set; }
    public double BoresightAzimuthDegrees { get; set; }
    public bool IsActive { get; set; }
    public long Revision { get; set; }
}
