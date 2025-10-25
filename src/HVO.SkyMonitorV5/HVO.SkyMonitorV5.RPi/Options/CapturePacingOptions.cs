using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configures dynamic capture pacing based on background stacker pressure.
/// </summary>
public sealed class CapturePacingOptions
{
    public bool Enabled { get; set; } = true;

    [Range(0, 10_000)]
    public int ElevatedAdditionalDelayMilliseconds { get; set; } = 250;

    [Range(0, 15_000)]
    public int HighAdditionalDelayMilliseconds { get; set; } = 500;

    [Range(0, 20_000)]
    public int CriticalAdditionalDelayMilliseconds { get; set; } = 1_000;

    [Range(0, 30_000)]
    public int RejectionPenaltyMilliseconds { get; set; } = 2_000;

    [Range(0, 600)]
    public int RejectionPenaltyDurationSeconds { get; set; } = 12;

    [Range(1, 10_000)]
    public int RampUpStepMilliseconds { get; set; } = 150;

    [Range(1, 10_000)]
    public int RampDownStepMilliseconds { get; set; } = 300;

    [Range(1, 30_000)]
    public int MaxDelayMilliseconds { get; set; } = 6_000;

    public void Normalize()
    {
        if (ElevatedAdditionalDelayMilliseconds < 0)
        {
            ElevatedAdditionalDelayMilliseconds = 0;
        }

        if (HighAdditionalDelayMilliseconds < 0)
        {
            HighAdditionalDelayMilliseconds = 0;
        }

        if (CriticalAdditionalDelayMilliseconds < 0)
        {
            CriticalAdditionalDelayMilliseconds = 0;
        }

        RampUpStepMilliseconds = Math.Clamp(RampUpStepMilliseconds, 1, 10_000);
        RampDownStepMilliseconds = Math.Clamp(RampDownStepMilliseconds, 1, 10_000);
        MaxDelayMilliseconds = Math.Clamp(MaxDelayMilliseconds, MinimumFrameDelayMilliseconds, 30_000);

        if (HighAdditionalDelayMilliseconds < ElevatedAdditionalDelayMilliseconds)
        {
            HighAdditionalDelayMilliseconds = ElevatedAdditionalDelayMilliseconds;
        }

        if (CriticalAdditionalDelayMilliseconds < HighAdditionalDelayMilliseconds)
        {
            CriticalAdditionalDelayMilliseconds = HighAdditionalDelayMilliseconds;
        }

        RejectionPenaltyMilliseconds = Math.Clamp(RejectionPenaltyMilliseconds, 0, MaxDelayMilliseconds);
        RejectionPenaltyDurationSeconds = Math.Clamp(RejectionPenaltyDurationSeconds, 0, 600);
    }

    private const int MinimumFrameDelayMilliseconds = 250;
}
