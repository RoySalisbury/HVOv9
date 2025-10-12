namespace HVO.SkyMonitorV5.Data.Configurations.Entities;

/// <summary>
/// Represents a configured camera adapter instance used by the runtime.
/// </summary>
public sealed class CameraAdapterConfigEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AdapterType { get; set; } = string.Empty;
    public int RigId { get; set; }
    public RigCatalogEntryEntity? Rig { get; set; }
}
