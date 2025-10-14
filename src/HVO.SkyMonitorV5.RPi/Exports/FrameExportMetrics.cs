using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Publishes metrics describing frame export dispatcher activity.
/// </summary>
public sealed class FrameExportMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly ObservableGauge<double> _queueDepthGauge;
    private readonly ObservableGauge<double> _inflightGauge;
    private readonly ObservableGauge<double> _pendingRetryGauge;
    private readonly Counter<long> _attemptCounter;
    private readonly Counter<long> _successCounter;
    private readonly Counter<long> _failureCounter;
    private readonly Counter<long> _droppedCounter;
    private readonly Histogram<double> _latencyHistogram;
    private readonly Histogram<long> _payloadBytesHistogram;
    private readonly Histogram<double> _queueLatencyHistogram;
    private readonly Histogram<double> _processingLatencyHistogram;
    private long _queueDepth;
    private long _inflight;
    private long _pendingRetry;
    private bool _disposed;

    public FrameExportMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create("HVO.SkyMonitor.FrameExport", version: "1.0.0");

        _queueDepthGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.frame_export.queue_depth",
            observeValue: ObserveQueueDepth,
            unit: "items",
            description: "Current frame export dispatcher queue depth.");

        _inflightGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.frame_export.inflight_dispatches",
            observeValue: ObserveInflight,
            unit: "dispatches",
            description: "Number of frame export dispatch tasks still running.");

        _pendingRetryGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.frame_export.pending_retries",
            observeValue: ObservePendingRetries,
            unit: "items",
            description: "Pending frame export payloads awaiting retry.");

        _attemptCounter = _meter.CreateCounter<long>(
            name: "hvo.skymonitor.frame_export.attempts",
            unit: "attempts",
            description: "Total frame export sink attempts.");

        _successCounter = _meter.CreateCounter<long>(
            name: "hvo.skymonitor.frame_export.successes",
            unit: "attempts",
            description: "Successful frame export sink attempts.");

        _failureCounter = _meter.CreateCounter<long>(
            name: "hvo.skymonitor.frame_export.failures",
            unit: "attempts",
            description: "Failed frame export sink attempts.");

        _droppedCounter = _meter.CreateCounter<long>(
            name: "hvo.skymonitor.frame_export.dropped",
            unit: "envelopes",
            description: "Frame export envelopes dropped because the dispatch queue was saturated or unavailable.");

        _latencyHistogram = _meter.CreateHistogram<double>(
            name: "hvo.skymonitor.frame_export.latency_ms",
            unit: "ms",
            description: "Latency in milliseconds for frame export sink attempts.");

        _payloadBytesHistogram = _meter.CreateHistogram<long>(
            name: "hvo.skymonitor.frame_export.payload_bytes",
            unit: "bytes",
            description: "Payload size in bytes delivered to frame export sinks.");

        _queueLatencyHistogram = _meter.CreateHistogram<double>(
            name: "hvo.skymonitor.frame_export.queue_latency_ms",
            unit: "ms",
            description: "Queue latency reported for exported frames when available.");

        _processingLatencyHistogram = _meter.CreateHistogram<double>(
            name: "hvo.skymonitor.frame_export.processing_ms",
            unit: "ms",
            description: "Processing duration reported for exported frames when available.");
    }

    public void ReportQueueEnqueued()
    {
        Interlocked.Increment(ref _queueDepth);
    }

    public void ReportQueueDequeued()
    {
        var value = Interlocked.Decrement(ref _queueDepth);
        if (value < 0)
        {
            Interlocked.Exchange(ref _queueDepth, 0);
        }
    }

    public void ReportDispatchStarted()
    {
        Interlocked.Increment(ref _inflight);
    }

    public void ReportDispatchCompleted()
    {
        var value = Interlocked.Decrement(ref _inflight);
        if (value < 0)
        {
            Interlocked.Exchange(ref _inflight, 0);
        }
    }

    public void RecordSinkAttempt(
        FrameExportStage stage,
        string sinkName,
        bool success,
        TimeSpan latency,
        long payloadBytes,
        double? queueLatencyMilliseconds,
        double? processingMilliseconds)
    {
        var stageName = stage.ToString();
        var tags = new TagList
        {
            { "stage", stageName },
            { "sink", sinkName }
        };

        _attemptCounter.Add(1, tags);

        if (success)
        {
            _successCounter.Add(1, tags);
        }
        else
        {
            _failureCounter.Add(1, tags);
        }

        var latencyValue = Math.Max(0d, latency.TotalMilliseconds);
        _latencyHistogram.Record(latencyValue, tags);

        if (payloadBytes >= 0)
        {
            _payloadBytesHistogram.Record(payloadBytes, tags);
        }

        if (queueLatencyMilliseconds.HasValue)
        {
            _queueLatencyHistogram.Record(Math.Max(0d, queueLatencyMilliseconds.Value), tags);
        }

        if (processingMilliseconds.HasValue)
        {
            _processingLatencyHistogram.Record(Math.Max(0d, processingMilliseconds.Value), tags);
        }
    }

    public void RecordDropped(FrameExportStage stage)
    {
        var tags = new TagList
        {
            { "stage", stage.ToString() }
        };

        _droppedCounter.Add(1, tags);
    }

    private double ObserveQueueDepth() => Volatile.Read(ref _queueDepth);

    private double ObserveInflight() => Volatile.Read(ref _inflight);

    private double ObservePendingRetries() => Volatile.Read(ref _pendingRetry);

    public void SetPendingRetryCount(long count)
    {
        Interlocked.Exchange(ref _pendingRetry, Math.Max(0L, count));
    }

    public long PendingRetryCount => Volatile.Read(ref _pendingRetry);

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
