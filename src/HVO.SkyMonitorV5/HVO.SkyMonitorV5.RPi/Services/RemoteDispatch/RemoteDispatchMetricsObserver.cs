#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;

/// <summary>
/// Publishes gauges for remote dispatch health derived from the frame state store.
/// </summary>
public sealed class RemoteDispatchMetricsObserver : IHostedService, IDisposable
{
    private readonly IFrameStateStore _frameStateStore;
    private readonly ILogger<RemoteDispatchMetricsObserver> _logger;
    private readonly Meter _meter;

    private ObservableGauge<double>? _successRateGauge;
    private ObservableGauge<double>? _averageLatencyGauge;
    private ObservableGauge<double>? _peakLatencyGauge;
    private ObservableGauge<double>? _lastLatencyGauge;
    private ObservableGauge<double>? _payloadBytesGauge;
    private ObservableGauge<double>? _successCountGauge;
    private ObservableGauge<double>? _failureCountGauge;
    private ObservableGauge<double>? _skippedCountGauge;

    public RemoteDispatchMetricsObserver(
        IFrameStateStore frameStateStore,
        IMeterFactory meterFactory,
        ILogger<RemoteDispatchMetricsObserver> logger)
    {
        _frameStateStore = frameStateStore ?? throw new ArgumentNullException(nameof(frameStateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (meterFactory is null)
        {
            throw new ArgumentNullException(nameof(meterFactory));
        }

        _meter = meterFactory.Create("HVO.SkyMonitor.RemoteDispatch", version: "1.0.0");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _successRateGauge = _meter.CreateObservableGauge<double>(
                name: "hvo.skymonitor.remote_dispatch.success_rate_percent",
                observeValue: ObserveSuccessRate,
                unit: "%",
                description: "Remote dispatch success rate over the in-memory telemetry window.");

            _averageLatencyGauge = _meter.CreateObservableGauge<double>(
                name: "hvo.skymonitor.remote_dispatch.latency_average_ms",
                observeValue: ObserveAverageLatency,
                unit: "ms",
                description: "Average latency in milliseconds for remote dispatch attempts.");

            _peakLatencyGauge = _meter.CreateObservableGauge<double>(
                name: "hvo.skymonitor.remote_dispatch.latency_peak_ms",
                observeValue: ObservePeakLatency,
                unit: "ms",
                description: "Peak latency in milliseconds recorded within the telemetry window.");

            _lastLatencyGauge = _meter.CreateObservableGauge<double>(
                name: "hvo.skymonitor.remote_dispatch.latency_last_ms",
                observeValue: ObserveLastLatency,
                unit: "ms",
                description: "Latency in milliseconds for the most recent remote dispatch attempt.");

            _payloadBytesGauge = _meter.CreateObservableGauge<double>(
                name: "hvo.skymonitor.remote_dispatch.payload_last_bytes",
                observeValue: ObserveLastPayloadBytes,
                unit: "bytes",
                description: "Payload size in bytes for the most recent remote dispatch attempt.");

            _successCountGauge = _meter.CreateObservableGauge<double>(
                name: "hvo.skymonitor.remote_dispatch.success_count",
                observeValue: ObserveSuccessCount,
                unit: "attempts",
                description: "Number of successful remote dispatch attempts within the telemetry window.");

            _failureCountGauge = _meter.CreateObservableGauge<double>(
                name: "hvo.skymonitor.remote_dispatch.failure_count",
                observeValue: ObserveFailureCount,
                unit: "attempts",
                description: "Number of failed remote dispatch attempts within the telemetry window.");

            _skippedCountGauge = _meter.CreateObservableGauge<double>(
                name: "hvo.skymonitor.remote_dispatch.skipped_count",
                observeValue: ObserveSkippedCount,
                unit: "attempts",
                description: "Number of skipped remote dispatch attempts within the telemetry window.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register remote dispatch gauges.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        DisposeGauges();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        DisposeGauges();
        _meter.Dispose();
    }

    private void DisposeGauges()
    {
        _successRateGauge = null;
        _averageLatencyGauge = null;
        _peakLatencyGauge = null;
        _lastLatencyGauge = null;
        _payloadBytesGauge = null;
        _successCountGauge = null;
        _failureCountGauge = null;
        _skippedCountGauge = null;
    }

    private double ObserveSuccessRate()
        => TryGetMetrics(out var metrics) ? metrics.SuccessRatePercent : double.NaN;

    private double ObserveAverageLatency()
    {
        if (TryGetMetrics(out var metrics) && metrics.AverageLatencyMilliseconds is { } value)
        {
            return value;
        }

        return double.NaN;
    }

    private double ObservePeakLatency()
    {
        if (TryGetMetrics(out var metrics) && metrics.PeakLatencyMilliseconds is { } value)
        {
            return value;
        }

        return double.NaN;
    }

    private double ObserveLastLatency()
    {
        if (TryGetMetrics(out var metrics) && metrics.LastLatencyMilliseconds is { } value)
        {
            return value;
        }

        return double.NaN;
    }

    private double ObserveLastPayloadBytes()
    {
        if (TryGetMetrics(out var metrics) && metrics.LastPayloadBytes is { } value)
        {
            return value;
        }

        return double.NaN;
    }

    private double ObserveSuccessCount()
        => TryGetMetrics(out var metrics) ? metrics.SuccessCount : 0d;

    private double ObserveFailureCount()
        => TryGetMetrics(out var metrics) ? metrics.FailureCount : 0d;

    private double ObserveSkippedCount()
        => TryGetMetrics(out var metrics) ? metrics.SkippedCount : 0d;

    private bool TryGetMetrics(out RemoteDispatchMetricsSnapshot metrics)
    {
        metrics = _frameStateStore.RemoteDispatchMetrics ?? RemoteDispatchMetricsSnapshotDefaults.Empty;
        return metrics.SampleCount > 0;
    }
}

internal static class RemoteDispatchMetricsSnapshotDefaults
{
    public static RemoteDispatchMetricsSnapshot Empty { get; } = new(
        GeneratedAt: DateTimeOffset.MinValue,
        SampleCount: 0,
        SuccessCount: 0,
        FailureCount: 0,
        SkippedCount: 0,
        SuccessRatePercent: 0,
        AverageLatencyMilliseconds: null,
        PeakLatencyMilliseconds: null,
        LastLatencyMilliseconds: null,
        LastPayloadBytes: null,
        LastPayloadContentType: null,
        LastPayloadExtension: null,
        FormatCounts: Array.Empty<RemoteDispatchFormatSummary>());
}
