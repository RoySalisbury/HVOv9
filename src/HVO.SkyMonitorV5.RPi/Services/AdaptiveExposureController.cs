using System;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Services;

public sealed class AdaptiveExposureController : IExposureController
{
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private readonly ILogger<AdaptiveExposureController>? _logger;
    private readonly IFrameStateStore _frameStateStore;
    private readonly object _sync = new();
    private ExposureSettings? _dayOverride;
    private ExposureSettings? _nightOverride;
    private DateTimeOffset _dayOverrideTimestamp;
    private DateTimeOffset _nightOverrideTimestamp;

    private static readonly TimeSpan RecommendationTtl = TimeSpan.FromMinutes(10);
    private const double MaxExposureStepFraction = 0.35;
    private const double MaxGainStepFraction = 0.35;
    private const int MinExposureMilliseconds = 1;
    private const int MaxExposureMilliseconds = 60_000;
    private const int MinGain = 0;
    private const int MaxGain = 500;

    public AdaptiveExposureController(
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        IFrameStateStore frameStateStore,
        ILogger<AdaptiveExposureController>? logger = null)
    {
        _optionsMonitor = optionsMonitor;
        _frameStateStore = frameStateStore ?? throw new ArgumentNullException(nameof(frameStateStore));
        _logger = logger;
    }

    public ExposureSettings CreateNextExposure(CameraConfiguration configuration)
    {
        var options = _optionsMonitor.CurrentValue;
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.Local).AddHours(options.DayNightTransitionHourOffset);
        var isDay = nowLocal.Hour is >= 6 and < 18;

        var overrideExposure = GetOverride(isDay);
        if (overrideExposure is not null)
        {
            _logger?.LogDebug(
                "Using override exposure {ExposureMs}ms / gain {Gain} for {LightingBucket} bucket.",
                overrideExposure.ExposureMilliseconds,
                overrideExposure.Gain,
                isDay ? "Day" : "Night");
            return overrideExposure;
        }

        var exposure = isDay ? options.DayExposureMilliseconds : options.NightExposureMilliseconds;
        var gain = isDay ? options.DayGain : options.NightGain;

        return new ExposureSettings(
            ExposureMilliseconds: exposure,
            Gain: gain,
            AutoExposure: false,
            AutoGain: false);
    }

    public void ApplyAnalysis(ExposureAnalysisResult analysis)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (analysis.SuggestedExposure is null)
        {
            _logger?.LogTrace(
                "Exposure analysis provided no recommendation for {LightingCondition} bucket (avg luminance {AverageLuminance:F1}).",
                analysis.LightingCondition,
                analysis.Metrics.AverageLuminance);
            return;
        }

        var bucket = DetermineBucket(analysis);
        var clamped = ClampToBounds(analysis.SuggestedExposure!);
        var options = _optionsMonitor.CurrentValue;
        var baseline = GetBaselineForSmoothing(bucket, options);
        var smoothed = SmoothRecommendation(baseline, clamped);

        if (smoothed.ExposureMilliseconds != clamped.ExposureMilliseconds || smoothed.Gain != clamped.Gain)
        {
            _logger?.LogTrace(
                "Smoothed exposure recommendation for {Bucket} bucket from {OriginalExposure}ms/{OriginalGain} to {SmoothedExposure}ms/{SmoothedGain}.",
                bucket,
                clamped.ExposureMilliseconds,
                clamped.Gain,
                smoothed.ExposureMilliseconds,
                smoothed.Gain);
        }

        _logger?.LogDebug(
            "Applying exposure recommendation for {Bucket} bucket: {ExposureMs}ms / gain {Gain} (avg luminance {AverageLuminance:F1}).",
            bucket,
            smoothed.ExposureMilliseconds,
            smoothed.Gain,
            analysis.Metrics.AverageLuminance);

        lock (_sync)
        {
            if (bucket == ExposureBucket.Day)
            {
                _dayOverride = smoothed;
                _dayOverrideTimestamp = DateTimeOffset.UtcNow;
            }
            else
            {
                _nightOverride = smoothed;
                _nightOverrideTimestamp = DateTimeOffset.UtcNow;
            }
        }

        _frameStateStore.UpdateExposureOverride(new ExposureOverrideUpdate(
            bucket == ExposureBucket.Day ? ExposureOverrideBucket.Day : ExposureOverrideBucket.Night,
            baseline,
            clamped,
            smoothed,
            DateTimeOffset.UtcNow,
            RecommendationTtl));
    }

    private ExposureSettings? GetOverride(bool isDay)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            if (isDay)
            {
                if (_dayOverride is not null && now - _dayOverrideTimestamp <= RecommendationTtl)
                {
                    return _dayOverride;
                }
            }
            else
            {
                if (_nightOverride is not null && now - _nightOverrideTimestamp <= RecommendationTtl)
                {
                    return _nightOverride;
                }
            }
        }

        return null;
    }

    private ExposureBucket DetermineBucket(ExposureAnalysisResult analysis)
        => analysis.LightingCondition switch
        {
            ExposureLightingCondition.Daylight => ExposureBucket.Day,
            ExposureLightingCondition.Night => ExposureBucket.Night,
            ExposureLightingCondition.Twilight => EstimateBucketFromExposure(analysis.CurrentExposure),
            _ => EstimateBucketFromExposure(analysis.CurrentExposure)
        };

    private ExposureBucket EstimateBucketFromExposure(ExposureSettings current)
    {
        var options = _optionsMonitor.CurrentValue;
        var threshold = (options.DayExposureMilliseconds + options.NightExposureMilliseconds) / 2.0;
        return current.ExposureMilliseconds >= threshold ? ExposureBucket.Night : ExposureBucket.Day;
    }

    private static ExposureSettings ClampToBounds(ExposureSettings suggested)
    {
        var clampedExposure = Math.Clamp(suggested.ExposureMilliseconds, MinExposureMilliseconds, MaxExposureMilliseconds);
        var clampedGain = Math.Clamp(suggested.Gain, MinGain, MaxGain);

        if (clampedExposure == suggested.ExposureMilliseconds && clampedGain == suggested.Gain)
        {
            return suggested;
        }

        return suggested with
        {
            ExposureMilliseconds = clampedExposure,
            Gain = clampedGain
        };
    }

    private ExposureSettings GetBaselineForSmoothing(ExposureBucket bucket, CameraPipelineOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (bucket == ExposureBucket.Day && _dayOverride is not null && now - _dayOverrideTimestamp <= RecommendationTtl)
            {
                return _dayOverride;
            }

            if (bucket == ExposureBucket.Night && _nightOverride is not null && now - _nightOverrideTimestamp <= RecommendationTtl)
            {
                return _nightOverride;
            }
        }

        return bucket == ExposureBucket.Day
            ? new ExposureSettings(options.DayExposureMilliseconds, options.DayGain, false, false)
            : new ExposureSettings(options.NightExposureMilliseconds, options.NightGain, false, false);
    }

    private static ExposureSettings SmoothRecommendation(ExposureSettings baseline, ExposureSettings suggested)
    {
        var exposure = LimitStep(baseline.ExposureMilliseconds, suggested.ExposureMilliseconds, MaxExposureStepFraction, MinExposureMilliseconds, MaxExposureMilliseconds);
        var gain = LimitStep(baseline.Gain, suggested.Gain, MaxGainStepFraction, MinGain, MaxGain);

        return suggested with
        {
            ExposureMilliseconds = exposure,
            Gain = gain
        };
    }

    private static int LimitStep(int current, int target, double fraction, int min, int max)
    {
        var delta = target - current;
        var maxStep = (int)Math.Round(Math.Max(1, Math.Abs(current) * fraction));
        var limitedDelta = Math.Clamp(delta, -maxStep, maxStep);
        var result = current + limitedDelta;
        return Math.Clamp(result, min, max);
    }

    private enum ExposureBucket
    {
        Day,
        Night
    }
}
