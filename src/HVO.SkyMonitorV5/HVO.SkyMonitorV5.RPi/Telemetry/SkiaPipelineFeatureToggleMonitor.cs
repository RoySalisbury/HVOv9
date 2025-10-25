using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HVO.SkyMonitorV5.RPi.Options;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

public interface ISkiaPipelineFeatureToggleMonitor
{
    void RecordFallback(string featureName);
}

/// <summary>
/// Publishes metrics and log events that describe the state of Skia pipeline feature toggles.
/// </summary>
public sealed class SkiaPipelineFeatureToggleMonitor : ISkiaPipelineFeatureToggleMonitor, IDisposable
{
    private readonly IOptionsMonitor<SkiaPipelineFeatureOptions> _optionsMonitor;
    private readonly ILogger<SkiaPipelineFeatureToggleMonitor> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _fallbackCounter;
    private readonly ObservableGauge<int> _rawLinearGauge;
    private readonly ObservableGauge<int> _processedEncoderGauge;
    private readonly IDisposable? _changeSubscription;
    private SkiaPipelineFeatureOptions _current;
    private bool _disposed;

    public SkiaPipelineFeatureToggleMonitor(
        IOptionsMonitor<SkiaPipelineFeatureOptions> optionsMonitor,
        IMeterFactory meterFactory,
        ILogger<SkiaPipelineFeatureToggleMonitor> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create("HVO.SkyMonitor.SkiaPipelineFeatures", version: "1.0.0");
        _fallbackCounter = _meter.CreateCounter<long>(
            name: "hvo.skymonitor.skia_pipeline.feature_fallbacks",
            unit: "events",
            description: "Counts fallback executions triggered by Skia pipeline feature toggles.");

        _rawLinearGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.skia_pipeline.raw_linear_payload_enabled",
            observeValue: ObserveRawLinearPayloadState,
            unit: "state",
            description: "Raw linear payload feature toggle state (1 enabled, 0 disabled).");

        _processedEncoderGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.skia_pipeline.processed_frame_encoder_enabled",
            observeValue: ObserveProcessedEncoderState,
            unit: "state",
            description: "Processed frame encoder feature toggle state (1 enabled, 0 disabled).");

        _current = optionsMonitor.CurrentValue;
        _changeSubscription = optionsMonitor.OnChange(OnOptionsChanged);
    }

    public void RecordFallback(string featureName)
    {
        _fallbackCounter.Add(1, new KeyValuePair<string, object?>("feature", featureName));
    }

    private int ObserveRawLinearPayloadState() => _optionsMonitor.CurrentValue.EnableRawLinearPayloads ? 1 : 0;

    private int ObserveProcessedEncoderState() => _optionsMonitor.CurrentValue.EnableProcessedFrameEncoder ? 1 : 0;

    private void OnOptionsChanged(SkiaPipelineFeatureOptions options)
    {
        LogStateChange(SkiaPipelineFeatureNames.RawLinearPayloads, _current.EnableRawLinearPayloads, options.EnableRawLinearPayloads);
        LogStateChange(SkiaPipelineFeatureNames.ProcessedFrameEncoder, _current.EnableProcessedFrameEncoder, options.EnableProcessedFrameEncoder);
        _current = options;
    }

    private void LogStateChange(string featureName, bool previous, bool current)
    {
        if (previous == current)
        {
            return;
        }

        _logger.LogInformation(
            "Skia pipeline feature '{Feature}' changed to {State}.",
            featureName,
            current ? "enabled" : "disabled");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _changeSubscription?.Dispose();
        _meter.Dispose();
    }
}

public static class SkiaPipelineFeatureNames
{
    public const string RawLinearPayloads = "raw-linear-payloads";
    public const string ProcessedFrameEncoder = "processed-frame-encoder";
}
