#nullable enable
using System;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents the outcome of analysing a captured frame for exposure tuning.
/// </summary>
public sealed record ExposureAnalysisResult(
    ExposureSettings CurrentExposure,
    ExposureSettings? SuggestedExposure,
    ExposureLightingCondition LightingCondition,
    ExposureMetrics Metrics,
    string? Notes = null);

/// <summary>
/// Basic statistics describing a captured image used for exposure analysis.
/// </summary>
public sealed record ExposureMetrics(
    double AverageLuminance,
    double MinimumLuminance,
    double MaximumLuminance,
    int SampleCount);

/// <summary>
/// High-level classification of the scene brightness to help guide exposure heuristics.
/// </summary>
public enum ExposureLightingCondition
{
    Unknown = 0,
    Daylight,
    Twilight,
    Night
}
