using System;
using System.Collections.Generic;

namespace HVO.SkyMonitorV5.RPi.Models.Cameras;

public sealed class CameraCatalogItem
{
    public int Id { get; set; }
    public long Revision { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public string DriverId { get; set; } = string.Empty;
    public bool IsSynthetic { get; set; }
    public string? SyntheticProfile { get; set; }
    public int SensorWidthPixels { get; set; }
    public int SensorHeightPixels { get; set; }
    public double PixelSizeMicrons { get; set; }
    public double? SensorCxPixels { get; set; }
    public double? SensorCyPixels { get; set; }
    public string ColorMode { get; set; } = string.Empty;
    public string SensorTechnology { get; set; } = string.Empty;
    public string BodyType { get; set; } = string.Empty;
    public string Cooling { get; set; } = string.Empty;
    public bool SupportsGainControl { get; set; }
    public bool SupportsExposureControl { get; set; }
    public bool SupportsTemperatureTelemetry { get; set; }
    public bool SupportsSoftwareBinning { get; set; }
    public IReadOnlyList<string> AdditionalTags { get; set; } = Array.Empty<string>();
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool IsActive { get; set; }
    public bool IsInUse { get; set; }
}
