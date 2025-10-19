namespace HVO.SkyMonitorV5.Data.Configurations.Entities;

/// <summary>
/// Represents a camera definition stored in the SkyMonitor catalog.
/// </summary>
public sealed class CameraCatalogCameraEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Catalog key (e.g., "MockASI174MM").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Friendly display name for UI and logging.
    /// </summary>
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

    /// <summary>
    /// JSON payload representing additional capability tags.
    /// </summary>
    public string AdditionalTagsJson { get; set; } = "[]";
}
