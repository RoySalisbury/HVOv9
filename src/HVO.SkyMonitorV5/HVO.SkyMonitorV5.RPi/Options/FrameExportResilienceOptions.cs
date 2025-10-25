using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

public sealed class FrameExportResilienceOptions
{
    public const string SectionName = "FrameExportResilience";

    /// <summary>Maximum retry attempts per sink attempt.</summary>
    [Range(0, 10)]
    public int RetryCount { get; set; } = 3;

    /// <summary>Initial backoff for the retry pipeline.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum backoff applied to retries.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Jitter range applied to retry backoff.</summary>
    public TimeSpan Jitter { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Optional timeout for sink operations.</summary>
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public void Normalize()
    {
        if (RetryCount < 0)
        {
            RetryCount = 0;
        }

        if (BaseDelay <= TimeSpan.Zero)
        {
            BaseDelay = TimeSpan.FromSeconds(1);
        }

        if (MaxDelay < BaseDelay)
        {
            MaxDelay = BaseDelay;
        }

        if (Jitter < TimeSpan.Zero)
        {
            Jitter = TimeSpan.Zero;
        }

        if (Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            Timeout = null;
        }
    }
}
