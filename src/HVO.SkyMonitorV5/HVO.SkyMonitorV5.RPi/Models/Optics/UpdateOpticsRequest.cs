using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.Optics;

public sealed class UpdateOpticsRequest : CreateOpticsRequest
{
    [Required]
    [Range(1, long.MaxValue)]
    public long Revision { get; set; }
}
