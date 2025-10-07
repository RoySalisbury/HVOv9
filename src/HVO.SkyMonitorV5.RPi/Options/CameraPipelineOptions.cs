using System;
using System.ComponentModel.DataAnnotations;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Options;

/// <summary>
/// Provides configuration defaults for the SkyMonitor v5 capture pipeline.
/// </summary>
public sealed class CameraPipelineOptions
{
    [Range(100, 10_000)]
    public int CaptureIntervalMilliseconds { get; set; } = 1_000;

    [Range(1, 32)]
    public int StackingFrameCount { get; set; } = 4;

    [Range(1, 240)]
    public int StackingBufferMinimumFrames { get; set; } = 24;

    [Range(0, 3_600)]
    public int StackingBufferIntegrationSeconds { get; set; } = 120;

    public bool EnableStacking { get; set; } = true;

    public bool EnableImageOverlays { get; set; } = false;

    public bool EnableMaskOverlay { get; set; } = false;

    public FrameFilterOption[] Filters { get; set; } = Array.Empty<FrameFilterOption>();

    public string[] FrameFilters { get; set; } = Array.Empty<string>();

    [Range(1, 60_000)]
    public int DayExposureMilliseconds { get; set; } = 1_000;

    [Range(1, 60_000)]
    public int NightExposureMilliseconds { get; set; } = 25_000;

    [Range(0, 500)]
    public int DayGain { get; set; } = 30;

    [Range(0, 500)]
    public int NightGain { get; set; } = 200;

    [Range(-12, 12)]
    public double DayNightTransitionHourOffset { get; set; } = 0;

    public string OverlayTextFormat { get; set; } = "yyyy-MM-dd HH:mm:ss zzz";
}
