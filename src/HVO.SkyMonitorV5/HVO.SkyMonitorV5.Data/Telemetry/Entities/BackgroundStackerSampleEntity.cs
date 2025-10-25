using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class BackgroundStackerSampleEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    public DateTimeOffset CapturedAtUtc { get; set; }

    [Required]
    public DateTimeOffset CapturedAtLocal { get; set; }

    public double QueueFillPercentage { get; set; }

    public int QueueDepth { get; set; }

    public int QueueCapacity { get; set; }

    public double? QueueLatencyMilliseconds { get; set; }

    public double? StackDurationMilliseconds { get; set; }

    public double? FilterDurationMilliseconds { get; set; }

    public int QueuePressureLevel { get; set; }

    public double? SecondsSinceLastCompleted { get; set; }

    public double QueueMemoryMegabytes { get; set; }
}
