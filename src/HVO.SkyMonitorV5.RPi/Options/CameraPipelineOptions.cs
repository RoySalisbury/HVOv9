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

    /// <summary>
    /// When enabled, capture runs on a producer queue and pipeline processing is executed by a background consumer task.
    /// </summary>
    public bool EnableAsyncProcessing { get; set; }

    public FrameFilterOption[] Filters { get; set; } = Array.Empty<FrameFilterOption>();

    public string[] FrameFilters { get; set; } = Array.Empty<string>();

    public BackgroundStackerOptions BackgroundStacker { get; set; } = new();

    public CapturePacingOptions CapturePacing { get; set; } = new();

    public RemoteDispatchOptions RemoteDispatch { get; set; } = new();

    public ImageEncodingOptions ProcessedImageEncoding { get; set; } = new();

    [Range(1, 60_000)]
    public int DayExposureMilliseconds { get; set; } = 50;

    [Range(1, 60_000)]
    public int NightExposureMilliseconds { get; set; } = 5_000;

    [Range(1, 60_000)]
    public int DayStartExposureMilliseconds { get; set; } = 2_000;

    [Range(1, 60_000)]
    public int NightStartExposureMilliseconds { get; set; } = 5_000;

    [Range(1, 60_000)]
    public int DayMinExposureMilliseconds { get; set; } = 1;

    [Range(1, 60_000)]
    public int DayMaxExposureMilliseconds { get; set; } = 60_000;

    [Range(1, 60_000)]
    public int NightMinExposureMilliseconds { get; set; } = 1;

    [Range(1, 60_000)]
    public int NightMaxExposureMilliseconds { get; set; } = 60_000;

    [Range(0, 500)]
    public int DayGain { get; set; } = 50;

    [Range(0, 500)]
    public int NightGain { get; set; } = 200;

    [Range(0, 500)]
    public int DayStartGain { get; set; } = 50;

    [Range(0, 500)]
    public int NightStartGain { get; set; } = 200;

    [Range(0, 500)]
    public int DayMinGain { get; set; } = 0;

    [Range(0, 500)]
    public int DayMaxGain { get; set; } = 500;

    [Range(0, 500)]
    public int NightMinGain { get; set; } = 0;

    [Range(0, 500)]
    public int NightMaxGain { get; set; } = 500;

    [Range(-12, 12)]
    public double DayNightTransitionHourOffset { get; set; } = 0;

    public string OverlayTextFormat { get; set; } = "yyyy-MM-dd HH:mm:ss zzz";
}
