using System;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Services;

public sealed class AdaptiveExposureController : IExposureController, IExposureBootstrapAware
{
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private readonly ILogger<AdaptiveExposureController>? _logger;
    private readonly IFrameStateStore _frameStateStore;
    private readonly object _sync = new();
    private ExposureSettings? _dayOverride;
    private ExposureSettings? _nightOverride;
    private DateTimeOffset _dayOverrideTimestamp;
    private DateTimeOffset _nightOverrideTimestamp;
    private ExposureSettings? _lastExposure;

    private static readonly TimeSpan RecommendationTtl = TimeSpan.FromMinutes(10);
    private const double MaxExposureStepFraction = 0.35;
    private const double MaxGainStepFraction = 0.35;
    private const int AbsoluteMinExposureMilliseconds = 1;
    private const int AbsoluteMaxExposureMilliseconds = 60_000;
    private const int AbsoluteMinGain = 0;
    private const int AbsoluteMaxGain = 500;

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

        var bucket = isDay ? ExposureBucket.Day : ExposureBucket.Night;
        var bounds = GetBounds(bucket, options);
        var overrideExposure = GetOverride(isDay);
        ExposureSettings resolved;

        if (overrideExposure is not null)
        {
            _logger?.LogDebug(
                "Using override exposure {ExposureMs}ms / gain {Gain} for {LightingBucket} bucket.",
                overrideExposure.ExposureMilliseconds,
                overrideExposure.Gain,
                isDay ? "Day" : "Night");

            resolved = ClampToBounds(overrideExposure, bounds);
        }
        else
        {
            bool isBootstrap;

            lock (_sync)
            {
                isBootstrap = _lastExposure is null;
            }

            var baselineExposure = isDay ? options.DayExposureMilliseconds : options.NightExposureMilliseconds;
            var baselineGain = isDay ? options.DayGain : options.NightGain;

            baselineExposure = Math.Clamp(baselineExposure, bounds.MinExposure, bounds.MaxExposure);
            baselineGain = Math.Clamp(baselineGain, bounds.MinGain, bounds.MaxGain);

            var bootstrapExposure = isDay ? options.DayStartExposureMilliseconds : options.NightStartExposureMilliseconds;
            var bootstrapGain = isDay ? options.DayStartGain : options.NightStartGain;

            bootstrapExposure = Math.Clamp(bootstrapExposure, bounds.MinExposure, bounds.MaxExposure);
            bootstrapGain = Math.Clamp(bootstrapGain, bounds.MinGain, bounds.MaxGain);

            var exposure = isBootstrap ? bootstrapExposure : baselineExposure;
            var gain = isBootstrap ? bootstrapGain : baselineGain;

            resolved = new ExposureSettings(
                ExposureMilliseconds: exposure,
                Gain: gain,
                AutoExposure: true,
                AutoGain: true);

            if (isBootstrap)
            {
                _logger?.LogDebug(
                    "Using bootstrap exposure {ExposureMs}ms / gain {Gain} for {LightingBucket} bucket.",
                    resolved.ExposureMilliseconds,
                    resolved.Gain,
                    isDay ? "Day" : "Night");
            }
        }

        resolved = ClampToBounds(resolved, bounds);

        lock (_sync)
        {
            _lastExposure = resolved;
        }

        return resolved;
    }

    public void ApplyAnalysis(ExposureAnalysisResult analysis)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        lock (_sync)
        {
            _lastExposure = analysis.CurrentExposure;
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
        var options = _optionsMonitor.CurrentValue;
        var bounds = GetBounds(bucket, options);
        var clamped = ClampToBounds(analysis.SuggestedExposure!, bounds);
        var baseline = GetBaselineForSmoothing(bucket, options, bounds);
        var smoothed = SmoothRecommendation(baseline, clamped, bounds);
        var final = ClampToBounds(smoothed, bounds);

        if (final.ExposureMilliseconds != clamped.ExposureMilliseconds || final.Gain != clamped.Gain)
        {
            _logger?.LogTrace(
                "Smoothed exposure recommendation for {Bucket} bucket from {OriginalExposure}ms/{OriginalGain} to {SmoothedExposure}ms/{SmoothedGain}.",
                bucket,
                clamped.ExposureMilliseconds,
                clamped.Gain,
                final.ExposureMilliseconds,
                final.Gain);
        }

        _logger?.LogDebug(
            "Applying exposure recommendation for {Bucket} bucket: {ExposureMs}ms / gain {Gain} (avg luminance {AverageLuminance:F1}).",
            bucket,
            final.ExposureMilliseconds,
            final.Gain,
            analysis.Metrics.AverageLuminance);

        lock (_sync)
        {
            if (bucket == ExposureBucket.Day)
            {
                _dayOverride = final;
                _dayOverrideTimestamp = DateTimeOffset.UtcNow;
            }
            else
            {
                _nightOverride = final;
                _nightOverrideTimestamp = DateTimeOffset.UtcNow;
            }
            _lastExposure = final;
        }

        _frameStateStore.UpdateExposureOverride(new ExposureOverrideUpdate(
            bucket == ExposureBucket.Day ? ExposureOverrideBucket.Day : ExposureOverrideBucket.Night,
            baseline,
            clamped,
            final,
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
        var dayBounds = GetBounds(ExposureBucket.Day, options);
        var nightBounds = GetBounds(ExposureBucket.Night, options);
        var dayExposure = Math.Clamp(options.DayExposureMilliseconds, dayBounds.MinExposure, dayBounds.MaxExposure);
        var nightExposure = Math.Clamp(options.NightExposureMilliseconds, nightBounds.MinExposure, nightBounds.MaxExposure);
        var threshold = (dayExposure + nightExposure) / 2.0;
        return current.ExposureMilliseconds >= threshold ? ExposureBucket.Night : ExposureBucket.Day;
    }

    private static ExposureSettings ClampToBounds(ExposureSettings suggested, ExposureBounds bounds)
    {
        var clampedExposure = Math.Clamp(suggested.ExposureMilliseconds, bounds.MinExposure, bounds.MaxExposure);
        var clampedGain = Math.Clamp(suggested.Gain, bounds.MinGain, bounds.MaxGain);

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

    private ExposureSettings GetBaselineForSmoothing(ExposureBucket bucket, CameraPipelineOptions options, ExposureBounds bounds)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (bucket == ExposureBucket.Day && _dayOverride is not null && now - _dayOverrideTimestamp <= RecommendationTtl)
            {
                return ClampToBounds(_dayOverride, bounds);
            }

            if (bucket == ExposureBucket.Night && _nightOverride is not null && now - _nightOverrideTimestamp <= RecommendationTtl)
            {
                return ClampToBounds(_nightOverride, bounds);
            }

            if (_lastExposure is not null)
            {
                var bucketForLast = EstimateBucketFromExposure(_lastExposure);
                if (bucketForLast == bucket)
                {
                    return ClampToBounds(_lastExposure, bounds);
                }
            }
        }

        var fallbackExposure = bucket == ExposureBucket.Day ? options.DayExposureMilliseconds : options.NightExposureMilliseconds;
        var fallbackGain = bucket == ExposureBucket.Day ? options.DayGain : options.NightGain;
        var fallback = new ExposureSettings(fallbackExposure, fallbackGain, false, false);
        return ClampToBounds(fallback, bounds);
    }

    public void BeginCaptureSession()
    {
        lock (_sync)
        {
            _lastExposure = null;
            _dayOverride = null;
            _nightOverride = null;
            _dayOverrideTimestamp = DateTimeOffset.MinValue;
            _nightOverrideTimestamp = DateTimeOffset.MinValue;
        }

        _logger?.LogTrace("Exposure controller capture session state reset to configuration defaults.");
    }

    private static ExposureSettings SmoothRecommendation(ExposureSettings baseline, ExposureSettings suggested, ExposureBounds bounds)
    {
        var exposure = LimitStep(baseline.ExposureMilliseconds, suggested.ExposureMilliseconds, MaxExposureStepFraction, bounds.MinExposure, bounds.MaxExposure);
        var gain = LimitStep(baseline.Gain, suggested.Gain, MaxGainStepFraction, bounds.MinGain, bounds.MaxGain);

        return suggested with
        {
            ExposureMilliseconds = exposure,
            Gain = gain
        };
    }

    private static ExposureBounds GetBounds(ExposureBucket bucket, CameraPipelineOptions options)
    {
        int minExposureRaw;
        int maxExposureRaw;
        int minGainRaw;
        int maxGainRaw;

        if (bucket == ExposureBucket.Day)
        {
            minExposureRaw = options.DayMinExposureMilliseconds;
            maxExposureRaw = options.DayMaxExposureMilliseconds;
            minGainRaw = options.DayMinGain;
            maxGainRaw = options.DayMaxGain;
        }
        else
        {
            minExposureRaw = options.NightMinExposureMilliseconds;
            maxExposureRaw = options.NightMaxExposureMilliseconds;
            minGainRaw = options.NightMinGain;
            maxGainRaw = options.NightMaxGain;
        }

        var minExposure = Math.Clamp(minExposureRaw, AbsoluteMinExposureMilliseconds, AbsoluteMaxExposureMilliseconds);
        var maxExposure = Math.Clamp(Math.Max(minExposure, maxExposureRaw), minExposure, AbsoluteMaxExposureMilliseconds);

        var minGain = Math.Clamp(minGainRaw, AbsoluteMinGain, AbsoluteMaxGain);
        var maxGain = Math.Clamp(Math.Max(minGain, maxGainRaw), minGain, AbsoluteMaxGain);

        return new ExposureBounds(minExposure, maxExposure, minGain, maxGain);
    }

    private static int LimitStep(int current, int target, double fraction, int min, int max)
    {
        var delta = target - current;
        var maxStep = (int)Math.Round(Math.Max(1, Math.Abs(current) * fraction));
        var limitedDelta = Math.Clamp(delta, -maxStep, maxStep);
        var result = current + limitedDelta;
        return Math.Clamp(result, min, max);
    }

    private readonly record struct ExposureBounds(int MinExposure, int MaxExposure, int MinGain, int MaxGain);

    private enum ExposureBucket
    {
        Day,
        Night
    }
}
