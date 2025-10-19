namespace HVO.SkyMonitorV5.RPi.Models.Optics;

public sealed class OpticsCatalogCamera
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int SensorWidthPixels { get; set; }
    public int SensorHeightPixels { get; set; }
}
