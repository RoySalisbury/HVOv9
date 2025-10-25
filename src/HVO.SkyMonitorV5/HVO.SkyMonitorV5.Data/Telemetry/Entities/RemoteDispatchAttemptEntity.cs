using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class RemoteDispatchAttemptEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    public DateTimeOffset AttemptedAtUtc { get; set; }

    [Required]
    public DateTimeOffset AttemptedAtLocal { get; set; }

    [Required]
    [MaxLength(64)]
    public string Mode { get; set; } = string.Empty;

    [Required]
    public int Outcome { get; set; }

    public double? LatencyMilliseconds { get; set; }

    public long? PayloadBytes { get; set; }

    [MaxLength(128)]
    public string? PayloadContentType { get; set; }

    [MaxLength(16)]
    public string? PayloadExtension { get; set; }

    [MaxLength(512)]
    public string? Message { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    [MaxLength(64)]
    public string? FormatKey { get; set; }
}
