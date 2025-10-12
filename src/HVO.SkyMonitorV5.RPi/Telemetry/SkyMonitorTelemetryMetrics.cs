using System;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class SkyMonitorTelemetryMetrics : IDisposable
{
    private readonly ISkyMonitorTelemetryIngestionQueue _queue;
    private readonly Meter _meter;
    private readonly ObservableGauge<double> _queueDepthGauge;
    private readonly ObservableGauge<double> _ingestionLatencyGauge;
    private readonly ObservableGauge<double> _retentionDurationGauge;
    private double _lastIngestionLatencyMs;
    private double _lastRetentionDurationMs;
    private readonly object _retentionLock = new();
    private TelemetryRetentionSnapshot _lastRetentionSnapshot = TelemetryRetentionSnapshot.Empty;
    private bool _disposed;

    public SkyMonitorTelemetryMetrics(IMeterFactory meterFactory, ISkyMonitorTelemetryIngestionQueue queue, ILogger<SkyMonitorTelemetryMetrics> logger)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));

        _meter = meterFactory.Create("HVO.SkyMonitor.Telemetry");

        _queueDepthGauge = _meter.CreateObservableGauge(
            name: "hvo_skymonitor_telemetry_queue_depth",
            observeValue: ObserveQueueDepth,
            unit: "items",
            description: "Current telemetry ingestion queue depth.");

        _ingestionLatencyGauge = _meter.CreateObservableGauge(
            name: "hvo_skymonitor_telemetry_ingestion_latency_ms",
            observeValue: ObserveIngestionLatency,
            unit: "ms",
            description: "Most recent telemetry ingestion latency in milliseconds.");

        _retentionDurationGauge = _meter.CreateObservableGauge(
            name: "hvo_skymonitor_telemetry_retention_duration_ms",
            observeValue: ObserveRetentionDuration,
            unit: "ms",
            description: "Most recent telemetry retention sweep duration in milliseconds.");

        logger.LogDebug("SkyMonitor telemetry metrics initialized.");
    }

    public void ReportIngestionLatency(TimeSpan latency)
    {
        var value = Math.Max(0, latency.TotalMilliseconds);
        Volatile.Write(ref _lastIngestionLatencyMs, value);
    }

    public void ReportRetentionSweepDuration(TimeSpan duration)
    {
        var value = Math.Max(0, duration.TotalMilliseconds);
        Volatile.Write(ref _lastRetentionDurationMs, value);
    }

    public void ReportRetentionCompletion(DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc, TelemetryRetentionSummary summary)
    {
        var duration = completedAtUtc - startedAtUtc;
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        Volatile.Write(ref _lastRetentionDurationMs, Math.Max(0, duration.TotalMilliseconds));

        var snapshot = new TelemetryRetentionSnapshot(
            startedAtUtc,
            completedAtUtc,
            duration,
            summary.RemoteDispatchPurged,
            summary.BackgroundStackerPurged,
            summary.CapturePacingPurged,
            summary.ProcessingQueuePurged,
            summary.FilterMetricsPurged,
            summary.TelemetryEventsPurged,
            summary.TotalPurged,
            summary.VacuumAttempted,
            summary.VacuumSucceeded);

        lock (_retentionLock)
        {
            _lastRetentionSnapshot = snapshot;
        }
    }

    private double ObserveQueueDepth() => _queue.PendingCount;

    private double ObserveIngestionLatency() => Volatile.Read(ref _lastIngestionLatencyMs);

    private double ObserveRetentionDuration() => Volatile.Read(ref _lastRetentionDurationMs);

    public TelemetryMetricsSnapshot GetTelemetrySnapshot()
    {
        return new TelemetryMetricsSnapshot(
            QueueDepth: _queue.PendingCount,
            LastIngestionLatencyMilliseconds: Volatile.Read(ref _lastIngestionLatencyMs),
            LastRetentionDurationMilliseconds: Volatile.Read(ref _lastRetentionDurationMs));
    }

    public TelemetryRetentionSnapshot GetRetentionSnapshot()
    {
        lock (_retentionLock)
        {
            return _lastRetentionSnapshot;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _meter.Dispose();
    }
}
