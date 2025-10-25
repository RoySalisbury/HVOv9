#nullable enable

namespace HVO.SkyMonitorV4.RPi.Models.AllSky;

public sealed record AllSkyExposureResponse(
    int Brightness,
    bool AutoBrightness,
    int AutoBrightnessTarget,
    int Gain,
    bool AutoGain,
    int AutoMaxGain,
    int DurationMilliseconds,
    bool AutoDuration,
    int AutoMaxDurationMilliseconds);
