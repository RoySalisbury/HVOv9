using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.Optics;

public class CreateOpticsRequest : IValidatableObject
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
    public string ProjectionModel { get; set; } = string.Empty;

    [Range(0.1, 5000.0)]
    public double FocalLengthMillimeters { get; set; } = 1.0;

    [Range(0.1, 360.0)]
    public double FieldOfViewXDegrees { get; set; } = 1.0;

    [Range(0.0, 360.0)]
    public double? FieldOfViewYDegrees { get; set; }

    [Range(-180.0, 180.0)]
    public double RollDegrees { get; set; }

    [MaxLength(64)]
    public string Kind { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FieldOfViewYDegrees is < 0.0)
        {
            yield return new ValidationResult("Field of view (Y) must be positive when specified.", new[] { nameof(FieldOfViewYDegrees) });
        }
    }
}
