#nullable enable
using System;

namespace HVO.SkyMonitorV5.RPi.Models;

public enum ExposureOverrideBucket
{
    Day,
    Night
}

public sealed record ExposureOverrideUpdate(
    ExposureOverrideBucket Bucket,
    ExposureSettings Baseline,
    ExposureSettings Target,
    ExposureSettings Applied,
    DateTimeOffset Timestamp,
    TimeSpan TimeToLive);

public sealed record ExposureOverrideState(
    ExposureOverrideBucket Bucket,
    ExposureSettings Baseline,
    ExposureSettings Target,
    ExposureSettings Applied,
    DateTimeOffset Timestamp,
    DateTimeOffset ExpiresAt);
