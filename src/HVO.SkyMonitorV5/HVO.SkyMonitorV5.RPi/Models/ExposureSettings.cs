namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents the exposure and gain configuration for a single capture.
/// </summary>
public sealed record ExposureSettings(
    int ExposureMilliseconds,
    int Gain,
    bool AutoExposure,
    bool AutoGain);
