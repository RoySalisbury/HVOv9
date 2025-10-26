using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.Rigs;

public sealed class CreateRigRequest
{
    [Required]
    [MaxLength(128)]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string CameraKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string OpticsKey { get; set; } = string.Empty;

    [Range(0.0, 90.0)]
    public double BoresightAltitudeDegrees { get; set; } = 90.0;

    [Range(0.0, 360.0)]
    public double BoresightAzimuthDegrees { get; set; } = 0.0;

    public bool IsActive { get; set; }
}
