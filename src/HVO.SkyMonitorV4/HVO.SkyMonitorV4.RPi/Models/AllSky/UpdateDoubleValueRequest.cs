#nullable enable

using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV4.RPi.Models.AllSky;

public sealed class UpdateDoubleValueRequest
{
    [Required]
    public double? Value { get; set; }
}
