using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.Adapters;

public sealed class AdapterSummary
{
    public int Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string AdapterType { get; set; } = string.Empty;

    [MaxLength(128)]
    public string RigKey { get; set; } = string.Empty;

    public string RigDisplayName { get; set; } = string.Empty;

    public bool RigIsActive { get; set; }

    public long RigRevision { get; set; }
}
