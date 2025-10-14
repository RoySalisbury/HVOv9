using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class FrameExportRetryEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    public Guid FrameId { get; set; }

    [Required]
    public int Stage { get; set; }

    [Required]
    [MaxLength(128)]
    public string SinkName { get; set; } = string.Empty;

    [Required]
    public DateTimeOffset EnqueuedAtUtc { get; set; }

    [Required]
    public DateTimeOffset NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    [Required]
    public int AttemptCount { get; set; }

    [Required]
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    [MaxLength(128)]
    public string? ContentType { get; set; }

    [MaxLength(16)]
    public string? FileExtension { get; set; }

    [Required]
    public string MetadataJson { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? LastErrorMessage { get; set; }
}
