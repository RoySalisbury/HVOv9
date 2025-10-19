using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.Optics;

public sealed class CreateOpticsRigRequest
{
    [Required]
    [MaxLength(128)]
    [RegularExpression("^[A-Za-z0-9_-]+$")] // slug-style rig key
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string CameraKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string LensKey { get; set; } = string.Empty;

    [Range(0.0, 90.0)]
    public double BoresightAltitudeDegrees { get; set; } = 90.0;

    [Range(0.0, 360.0)]
    public double BoresightAzimuthDegrees { get; set; } = 0.0;

    public bool IsActive { get; set; }
}
