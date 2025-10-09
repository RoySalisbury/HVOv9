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
}
