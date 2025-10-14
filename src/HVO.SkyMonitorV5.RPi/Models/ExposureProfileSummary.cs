using System;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Describes the configured exposure and gain profiles for each lighting bucket.
/// </summary>
public sealed record ExposureProfileSummary(
    ExposureProfileBucketSummary Day,
    ExposureProfileBucketSummary Night);

/// <summary>
/// Represents the exposure and gain configuration for a single lighting bucket.
/// </summary>
public sealed record ExposureProfileBucketSummary(
    string Name,
    int BaselineExposureMilliseconds,
    int StartExposureMilliseconds,
    int MinExposureMilliseconds,
    int MaxExposureMilliseconds,
    int BaselineGain,
    int StartGain,
    int MinGain,
    int MaxGain)
{
    public ExposureProfileBucketSummary Normalize()
    {
        const int minExposureBound = 1;
        const int maxExposureBound = 60_000;
        const int minGainBound = 0;
        const int maxGainBound = 500;

        var minExposure = Math.Clamp(MinExposureMilliseconds, minExposureBound, maxExposureBound);
        var maxExposure = Math.Clamp(Math.Max(minExposure, MaxExposureMilliseconds), minExposure, maxExposureBound);
        var startExposure = Math.Clamp(StartExposureMilliseconds, minExposure, maxExposure);
        var baselineExposure = Math.Clamp(BaselineExposureMilliseconds, minExposure, maxExposure);

        var minGain = Math.Clamp(MinGain, minGainBound, maxGainBound);
        var maxGain = Math.Clamp(Math.Max(minGain, MaxGain), minGain, maxGainBound);
        var startGain = Math.Clamp(StartGain, minGain, maxGain);
        var baselineGain = Math.Clamp(BaselineGain, minGain, maxGain);

        return this with
        {
            MinExposureMilliseconds = minExposure,
            MaxExposureMilliseconds = maxExposure,
            StartExposureMilliseconds = startExposure,
            BaselineExposureMilliseconds = baselineExposure,
            MinGain = minGain,
            MaxGain = maxGain,
            StartGain = startGain,
            BaselineGain = baselineGain
        };
    }
}
