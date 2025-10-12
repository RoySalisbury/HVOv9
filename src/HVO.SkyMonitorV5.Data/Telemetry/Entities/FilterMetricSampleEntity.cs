using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class FilterMetricSampleEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    public DateTimeOffset CapturedAtUtc { get; set; }

    [Required]
    public DateTimeOffset CapturedAtLocal { get; set; }

    [Required]
    [MaxLength(128)]
    public string FilterName { get; set; } = string.Empty;

    public long AppliedCount { get; set; }

    public double? LastDurationMilliseconds { get; set; }

    public double? AverageDurationMilliseconds { get; set; }
}
