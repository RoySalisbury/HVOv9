using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configures retention policies for the SkyMonitor telemetry store.
/// </summary>
public sealed class SkyMonitorTelemetryRetentionOptions
{
    /// <summary>
    /// Gets or sets the interval between retention sweeps.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00", ConvertValueInInvariantCulture = true)]
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets whether to run VACUUM after successful purges.
    /// </summary>
    public bool VacuumAfterPurge { get; set; } = true;

    /// <summary>
    /// Retention policy for remote dispatch attempts.
    /// </summary>
    [Required]
    public TelemetryRetentionPolicy RemoteDispatch { get; set; } = TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), maxRecords: 5_000);

    /// <summary>
    /// Retention policy for background stacker samples.
    /// </summary>
    [Required]
    public TelemetryRetentionPolicy BackgroundStacker { get; set; } = TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), maxRecords: 15_000);

    /// <summary>
    /// Retention policy for capture pacing samples.
    /// </summary>
    [Required]
    public TelemetryRetentionPolicy CapturePacing { get; set; } = TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), maxRecords: 15_000);

    /// <summary>
    /// Retention policy for processing queue samples.
    /// </summary>
    [Required]
    public TelemetryRetentionPolicy ProcessingQueue { get; set; } = TelemetryRetentionPolicy.Create(TimeSpan.FromDays(14), maxRecords: 15_000);

    /// <summary>
    /// Retention policy for filter metric samples.
    /// </summary>
    [Required]
    public TelemetryRetentionPolicy FilterMetrics { get; set; } = TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), maxRecords: 5_000);

    /// <summary>
    /// Retention policy for structured telemetry events.
    /// </summary>
    [Required]
    public TelemetryRetentionPolicy TelemetryEvents { get; set; } = TelemetryRetentionPolicy.Create(TimeSpan.FromDays(30), maxRecords: 20_000);

    public const string SectionName = "SkyMonitor:Telemetry:Retention";
}

/// <summary>
/// Represents a retention policy with optional age- and count-based limits.
/// </summary>
public sealed class TelemetryRetentionPolicy
{
    private TelemetryRetentionPolicy()
    {
    }

    /// <summary>
    /// Gets or sets the maximum age for retained records. Null disables age-based purging.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "365.00:00:00", ConvertValueInInvariantCulture = true)]
    public TimeSpan? MaxAge { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of records to keep. Null disables record-count purging.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? MaxRecords { get; set; }

    public static TelemetryRetentionPolicy Create(TimeSpan? maxAge, int? maxRecords)
        => new()
        {
            MaxAge = maxAge,
            MaxRecords = maxRecords
        };
}
