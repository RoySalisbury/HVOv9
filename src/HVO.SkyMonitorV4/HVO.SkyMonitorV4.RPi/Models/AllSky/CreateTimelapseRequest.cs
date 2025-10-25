#nullable enable

using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV4.RPi.Models.AllSky;

public sealed class CreateTimelapseRequest
{
    [Required]
    public DateTimeOffset? StartTimeUtc { get; set; }

    [Range(1, 86400)]
    public int DurationSeconds { get; set; } = 3600;

    [MaxLength(64)]
    public string? OutputPrefix { get; set; }
}
