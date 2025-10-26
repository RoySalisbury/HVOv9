using System;
using System.ComponentModel.DataAnnotations;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Configuration for the background stacking worker.
/// </summary>
public sealed class BackgroundStackerOptions
{
    public bool Enabled { get; set; }

    [Range(1, 1_000)]
    public int QueueCapacity { get; set; } = 32;

    public BackgroundStackerOverflowPolicy OverflowPolicy { get; set; } = BackgroundStackerOverflowPolicy.Block;

    public BackgroundStackerCompressionMode CompressionMode { get; set; } = BackgroundStackerCompressionMode.None;

    [Range(1, 600)]
    public int RestartDelaySeconds { get; set; } = 5;

    public AdaptiveQueueOptions AdaptiveQueue { get; set; } = new();
}

/// <summary>
/// Configuration for adaptive background queue tuning.
/// </summary>
public sealed class AdaptiveQueueOptions
{
    public bool Enabled { get; set; }

    [Range(1, 1_000)]
    public int MinCapacity { get; set; } = 24;

    [Range(1, 1_000)]
    public int MaxCapacity { get; set; } = 64;

    [Range(1, 1_000)]
    public int IncreaseStep { get; set; } = 8;

    [Range(1, 1_000)]
    public int DecreaseStep { get; set; } = 8;

    [Range(0, 100)]
    public double ScaleUpThresholdPercent { get; set; } = 80d;

    [Range(0, 100)]
    public double ScaleDownThresholdPercent { get; set; } = 40d;

    [Range(1, 600)]
    public int EvaluationWindowSeconds { get; set; } = 15;

    [Range(1, 600)]
    public int CooldownSeconds { get; set; } = 45;

    public AdaptiveQueueOptions Clone()
        => new()
        {
            Enabled = Enabled,
            MinCapacity = MinCapacity,
            MaxCapacity = MaxCapacity,
            IncreaseStep = IncreaseStep,
            DecreaseStep = DecreaseStep,
            ScaleUpThresholdPercent = ScaleUpThresholdPercent,
            ScaleDownThresholdPercent = ScaleDownThresholdPercent,
            EvaluationWindowSeconds = EvaluationWindowSeconds,
            CooldownSeconds = CooldownSeconds
        };

    public void Normalize()
    {
        if (MaxCapacity < MinCapacity)
        {
            (MinCapacity, MaxCapacity) = (MaxCapacity, MinCapacity);
        }

        if (IncreaseStep <= 0)
        {
            IncreaseStep = 1;
        }

        if (DecreaseStep <= 0)
        {
            DecreaseStep = 1;
        }

        ScaleUpThresholdPercent = Math.Clamp(ScaleUpThresholdPercent, 0d, 100d);
        ScaleDownThresholdPercent = Math.Clamp(ScaleDownThresholdPercent, 0d, ScaleUpThresholdPercent);

        EvaluationWindowSeconds = Math.Clamp(EvaluationWindowSeconds, 1, 600);
        CooldownSeconds = Math.Clamp(CooldownSeconds, 1, 600);
    }
}
