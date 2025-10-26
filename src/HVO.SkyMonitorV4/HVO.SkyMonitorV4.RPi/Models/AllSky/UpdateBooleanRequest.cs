#nullable enable

using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV4.RPi.Models.AllSky;

public sealed class UpdateBooleanRequest
{
    [Required]
    public bool? Value { get; set; }
}
