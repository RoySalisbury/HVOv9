using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class TelemetryEventEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    public DateTimeOffset OccurredAtUtc { get; set; }

    [Required]
    public DateTimeOffset OccurredAtLocal { get; set; }

    [Required]
    [MaxLength(128)]
    public string Category { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string EventType { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string Severity { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Summary { get; set; }

    [MaxLength(4096)]
    public string? Detail { get; set; }

    public string? PropertiesJson { get; set; }
}
