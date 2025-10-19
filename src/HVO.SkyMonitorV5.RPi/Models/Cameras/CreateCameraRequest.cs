using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.Cameras;

public class CreateCameraRequest : IValidatableObject
{
    private const string KeyPattern = "^[A-Za-z0-9_-]+$";

    [Required]
    [MaxLength(128)]
    [RegularExpression(KeyPattern)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Manufacturer { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Model { get; set; } = string.Empty;

    [MaxLength(64)]
    public string DriverVersion { get; set; } = string.Empty;

    [MaxLength(128)]
    public string AdapterName { get; set; } = string.Empty;

    [MaxLength(128)]
    public string DriverId { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SyntheticProfile { get; set; } = string.Empty;

    public bool IsSynthetic { get; set; }

    [Range(1, 20000)]
    public int SensorWidthPixels { get; set; } = 1;

    [Range(1, 20000)]
    public int SensorHeightPixels { get; set; } = 1;

    [Range(0.1, 100.0)]
    public double PixelSizeMicrons { get; set; } = 1.0;

    [Range(0.0, 20000.0)]
    public double? SensorCxPixels { get; set; }

    [Range(0.0, 20000.0)]
    public double? SensorCyPixels { get; set; }

    [MaxLength(64)]
    public string ColorMode { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SensorTechnology { get; set; } = string.Empty;

    [MaxLength(64)]
    public string BodyType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Cooling { get; set; } = string.Empty;

    public bool SupportsGainControl { get; set; } = true;

    public bool SupportsExposureControl { get; set; } = true;

    public bool SupportsTemperatureTelemetry { get; set; }

    public bool SupportsSoftwareBinning { get; set; }

    public bool IsActive { get; set; } = true;

    public IReadOnlyList<string> AdditionalTags { get; set; } = new List<string>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SensorWidthPixels <= 0)
        {
            yield return new ValidationResult("Sensor width must be greater than zero.", new[] { nameof(SensorWidthPixels) });
        }

        if (SensorHeightPixels <= 0)
        {
            yield return new ValidationResult("Sensor height must be greater than zero.", new[] { nameof(SensorHeightPixels) });
        }

        if (PixelSizeMicrons <= 0)
        {
            yield return new ValidationResult("Pixel size must be greater than zero.", new[] { nameof(PixelSizeMicrons) });
        }
    }
}
