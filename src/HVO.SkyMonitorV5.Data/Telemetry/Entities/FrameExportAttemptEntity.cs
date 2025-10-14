using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class FrameExportAttemptEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    public DateTimeOffset AttemptedAtUtc { get; set; }

    [Required]
    public DateTimeOffset AttemptedAtLocal { get; set; }

    [Required]
    public Guid FrameId { get; set; }

    [Required]
    public int Stage { get; set; }

    [Required]
    [MaxLength(128)]
    public string SinkName { get; set; } = string.Empty;

    [Required]
    public bool Success { get; set; }

    public double? LatencyMilliseconds { get; set; }

    public long? PayloadBytes { get; set; }

    [MaxLength(128)]
    public string? PayloadContentType { get; set; }

    [MaxLength(16)]
    public string? PayloadExtension { get; set; }

    public double? QueueLatencyMilliseconds { get; set; }

    public double? ProcessingMilliseconds { get; set; }

    public int? FramesStacked { get; set; }

    public int? IntegrationMilliseconds { get; set; }

    public double? FullPipelineMilliseconds { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }
}
