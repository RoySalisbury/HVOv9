#nullable enable

using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV4.RPi.Models.AllSky;

public sealed class UpdateIntValueRequest
{
    [Required]
    public int? Value { get; set; }
}
