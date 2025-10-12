using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.Data.Telemetry.Entities;

public sealed class ProcessingQueueSampleEntity
{
    [Key]
    public long Id { get; set; }

    [Required]
    public DateTimeOffset CapturedAtUtc { get; set; }

    [Required]
    public DateTimeOffset CapturedAtLocal { get; set; }

    public bool Enabled { get; set; }

    public int Capacity { get; set; }

    public int Depth { get; set; }

    public int BackpressureEvents { get; set; }

    public double LastEnqueueWaitMilliseconds { get; set; }

    public double PeakEnqueueWaitMilliseconds { get; set; }

    public double AverageEnqueueWaitMilliseconds { get; set; }

    public double LastProcessingMilliseconds { get; set; }

    public double PeakProcessingMilliseconds { get; set; }

    public double AverageProcessingMilliseconds { get; set; }
}
