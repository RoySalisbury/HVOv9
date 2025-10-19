using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Models.System;

public sealed class SystemTelemetryRetentionConfigurationResponse
{
    public required TimeSpan SweepInterval { get; init; }

    public required bool VacuumAfterPurge { get; init; }

    public required TelemetryRetentionPolicyModel RemoteDispatch { get; init; }

    public required TelemetryRetentionPolicyModel FrameExports { get; init; }

    public required TelemetryRetentionPolicyModel BackgroundStacker { get; init; }

    public required TelemetryRetentionPolicyModel CapturePacing { get; init; }

    public required TelemetryRetentionPolicyModel ProcessingQueue { get; init; }

    public required TelemetryRetentionPolicyModel FilterMetrics { get; init; }

    public required TelemetryRetentionPolicyModel TelemetryEvents { get; init; }

    public required long Revision { get; init; }
}

public sealed class UpdateSystemTelemetryRetentionRequest
{
    [Range(0, long.MaxValue)]
    public long Revision { get; set; }

    [Range(typeof(double), "0.1", "86400", ConvertValueInInvariantCulture = true)]
    public double SweepIntervalSeconds { get; set; } = TimeSpan.FromMinutes(15).TotalSeconds;

    public bool VacuumAfterPurge { get; set; } = true;

    [Required]
    public TelemetryRetentionPolicyModel RemoteDispatch { get; set; } = new();

    [Required]
    public TelemetryRetentionPolicyModel FrameExports { get; set; } = new();

    [Required]
    public TelemetryRetentionPolicyModel BackgroundStacker { get; set; } = new();

    [Required]
    public TelemetryRetentionPolicyModel CapturePacing { get; set; } = new();

    [Required]
    public TelemetryRetentionPolicyModel ProcessingQueue { get; set; } = new();

    [Required]
    public TelemetryRetentionPolicyModel FilterMetrics { get; set; } = new();

    [Required]
    public TelemetryRetentionPolicyModel TelemetryEvents { get; set; } = new();
}

public sealed class TelemetryRetentionPolicyModel
{
    [Range(typeof(double), "0.0", "31536000", ConvertValueInInvariantCulture = true)]
    public double? MaxAgeSeconds { get; set; }
        = TimeSpan.FromDays(30).TotalSeconds;

    [Range(1, int.MaxValue)]
    public int? MaxRecords { get; set; }
        = 5_000;
}
