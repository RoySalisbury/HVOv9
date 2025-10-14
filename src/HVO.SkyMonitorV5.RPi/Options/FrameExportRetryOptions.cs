using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configures the frame export retry queue behavior.
/// </summary>
public sealed class FrameExportRetryOptions
{
    public const string SectionName = "FrameExportRetry";

    /// <summary>
    /// Gets or sets a value indicating whether the retry queue is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts before a payload is abandoned.
    /// </summary>
    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the initial backoff delay applied after the first failure.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the multiplier applied to the backoff delay after each failure.
    /// </summary>
    [Range(1.0, 10.0)]
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the maximum backoff applied between retry attempts.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the optional jitter to add/subtract from the computed backoff.
    /// </summary>
    public TimeSpan MaxJitter { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of pending retry items allowed.
    /// </summary>
    [Range(0, 5000)]
    public int MaxQueueSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets the batch size for retry processing.
    /// </summary>
    [Range(1, 100)]
    public int BatchSize { get; set; } = 5;

    /// <summary>
    /// Gets or sets the polling interval used when the queue is idle.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public void Normalize()
    {
        if (MaxAttempts < 1)
        {
            MaxAttempts = 1;
        }

        if (BackoffMultiplier < 1.0)
        {
            BackoffMultiplier = 1.0;
        }

        if (InitialBackoff <= TimeSpan.Zero)
        {
            InitialBackoff = TimeSpan.FromSeconds(10);
        }

        if (MaxBackoff < InitialBackoff)
        {
            MaxBackoff = InitialBackoff;
        }

        if (MaxJitter < TimeSpan.Zero)
        {
            MaxJitter = TimeSpan.Zero;
        }

        if (MaxQueueSize < 0)
        {
            MaxQueueSize = 0;
        }

        if (BatchSize < 1)
        {
            BatchSize = 1;
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            PollInterval = TimeSpan.FromSeconds(5);
        }
    }
}
