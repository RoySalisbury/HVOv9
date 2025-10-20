using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.Adapters;

public sealed class CreateAdapterRequest
{
    [Required]
    [MaxLength(128)]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string AdapterType { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string RigKey { get; set; } = string.Empty;
}
