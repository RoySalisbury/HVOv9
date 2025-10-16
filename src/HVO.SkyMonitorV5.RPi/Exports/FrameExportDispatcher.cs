using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Background dispatcher that fans out frame export envelopes to registered sinks.
/// </summary>
public sealed class FrameExportDispatcher : BackgroundService, IFrameExportDispatcher, IDisposable
{
    private readonly Channel<FrameExportEnvelope> _channel;
    private readonly IReadOnlyList<IFrameExportSink> _sinks;
    private readonly ILogger<FrameExportDispatcher> _logger;
    private readonly FrameExportDispatcherOptions _options;
    private readonly IObservatoryClock _clock;
    private readonly ISkyMonitorTelemetryRecorder? _telemetryRecorder;
    private readonly FrameExportMetrics _metrics;
    private readonly IFrameExportRetryQueue? _retryQueue;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly object _inflightLock = new();
    private readonly List<Task> _inflight = new();

    public FrameExportDispatcher(
        IOptions<FrameExportDispatcherOptions> options,
        IEnumerable<IFrameExportSink> sinks,
        IObservatoryClock clock,
        ISkyMonitorTelemetryRecorder? telemetryRecorder,
        FrameExportMetrics metrics,
        IFrameExportRetryQueue? retryQueue,
        ILogger<FrameExportDispatcher> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sinks = (sinks ?? throw new ArgumentNullException(nameof(sinks))).ToArray();
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _telemetryRecorder = telemetryRecorder;
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _retryQueue = retryQueue;

        var capacity = Math.Max(1, _options.ChannelCapacity);
        var boundedOptions = new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = _options.ToBoundedMode()
        };

        _channel = Channel.CreateBounded<FrameExportEnvelope>(boundedOptions);
        _concurrencyLimiter = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
    }

    public bool TryEnqueue(FrameExportEnvelope envelope)
    {
        if (_channel.Writer.TryWrite(envelope))
        {
            _metrics.ReportQueueEnqueued();
            LogQueued(envelope, immediate: true);
            return true;
        }

        HandleChannelFull(envelope, immediate: true);
        return false;
    }

    public async ValueTask<bool> EnqueueAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            await _channel.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
            _metrics.ReportQueueEnqueued();
            LogQueued(envelope, immediate: false);
            return true;
        }
        catch (ChannelClosedException)
        {
            _metrics.RecordDropped(envelope.Stage);
            _logger.LogWarning(
                "Frame export dispatcher channel closed while enqueuing payload {FrameId} ({Stage}).", 
                envelope.FrameId,
                envelope.Stage);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Frame export dispatcher starting with {SinkCount} sink(s).", _sinks.Count);

        try
        {
            await foreach (var envelope in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                _metrics.ReportQueueDequeued();
                await _concurrencyLimiter.WaitAsync(stoppingToken).ConfigureAwait(false);
                var dispatchTask = DispatchAsync(envelope, stoppingToken);
                TrackInflight(dispatchTask);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Cancellation requested; fall through to draining inflight tasks.
        }
        finally
        {
            await DrainInflightAsync(stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Frame export dispatcher stopped.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken)
    {
        _metrics.ReportDispatchStarted();
        try
        {
            var handled = false;

            foreach (var sink in _sinks)
            {
                if (!sink.SupportsStage(envelope.Stage))
                {
                    continue;
                }

                handled = true;

                var attemptStartedAtUtc = _clock.UtcNow;
                var attemptStartedAtLocal = _clock.LocalNow;
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    var result = await sink.ExportAsync(envelope, cancellationToken).ConfigureAwait(false);
                    stopwatch.Stop();

                    var success = result.IsSuccessful && result.Value;
                    var errorMessage = result.IsFailure ? result.Error?.Message : success ? null : "Sink reported unsuccessful result.";

                    RecordFrameExportAttemptTelemetry(
                        envelope,
                        sink.Name,
                        attemptStartedAtUtc,
                        attemptStartedAtLocal,
                        success,
                        stopwatch.Elapsed,
                        errorMessage);

                    _metrics.RecordSinkAttempt(
                        envelope.Stage,
                        sink.Name,
                        success,
                        stopwatch.Elapsed,
                        envelope.Payload.Length,
                        envelope.Metadata.QueueLatencyMilliseconds,
                        envelope.Metadata.ProcessingMilliseconds);

                    if (result.IsFailure)
                    {
                        _logger.LogWarning(
                            result.Error,
                            "Frame export sink {Sink} reported failure for payload {FrameId} ({Stage}).",
                            sink.Name,
                            envelope.FrameId,
                            envelope.Stage);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Frame export sink {Sink} accepted payload {FrameId} ({Stage}).",
                            sink.Name,
                            envelope.FrameId,
                            envelope.Stage);
                    }

                    if (!success)
                    {
                        await TryScheduleRetryAsync(envelope, sink.Name, attemptCount: 1, errorMessage, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();

                    RecordFrameExportAttemptTelemetry(
                        envelope,
                        sink.Name,
                        attemptStartedAtUtc,
                        attemptStartedAtLocal,
                        success: false,
                        stopwatch.Elapsed,
                        ex.Message);

                    _metrics.RecordSinkAttempt(
                        envelope.Stage,
                        sink.Name,
                        success: false,
                        stopwatch.Elapsed,
                        envelope.Payload.Length,
                        envelope.Metadata.QueueLatencyMilliseconds,
                        envelope.Metadata.ProcessingMilliseconds);

                    _logger.LogError(
                        ex,
                        "Frame export sink {Sink} threw an exception for payload {FrameId} ({Stage}).",
                        sink.Name,
                        envelope.FrameId,
                        envelope.Stage);

                    await TryScheduleRetryAsync(envelope, sink.Name, attemptCount: 1, ex.Message, cancellationToken).ConfigureAwait(false);
                }
            }

            if (!handled)
            {
                _metrics.RecordDropped(envelope.Stage);
                _logger.LogTrace(
                    "No export sinks registered for stage {Stage}. Dropping payload {FrameId}.",
                    envelope.Stage,
                    envelope.FrameId);
            }
        }
        finally
        {
            _metrics.ReportDispatchCompleted();
            _concurrencyLimiter.Release();
        }
    }

    private void TrackInflight(Task task)
    {
        lock (_inflightLock)
        {
            _inflight.RemoveAll(static t => t.IsCompleted);
            _inflight.Add(task);
        }
    }

    private async Task DrainInflightAsync(CancellationToken stoppingToken)
    {
        Task[] tasks;
        lock (_inflightLock)
        {
            tasks = _inflight.Where(static t => !t.IsCompleted).ToArray();
        }

        if (tasks.Length == 0)
        {
            return;
        }

        var timeout = _options.DrainTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (timeout > TimeSpan.Zero)
        {
            cts.CancelAfter(timeout);
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Frame export dispatcher terminating with {PendingCount} pending task(s) after waiting {Timeout}.",
                tasks.Length,
                timeout);
        }
    }

    private void LogQueued(FrameExportEnvelope envelope, bool immediate)
    {
        if (_logger.IsEnabled(immediate ? LogLevel.Trace : LogLevel.Debug))
        {
            _logger.Log(immediate ? LogLevel.Trace : LogLevel.Debug,
                "Enqueued frame export {FrameId} ({Stage}).",
                envelope.FrameId,
                envelope.Stage);
        }
    }

    private void HandleChannelFull(FrameExportEnvelope envelope, bool immediate)
    {
        var level = _options.FullMode == FrameExportChannelFullMode.Wait ? LogLevel.Warning : LogLevel.Debug;
        _metrics.RecordDropped(envelope.Stage);
        _logger.Log(level,
            "Frame export dispatcher channel is full; dropping payload {FrameId} ({Stage}).",
            envelope.FrameId,
            envelope.Stage);
    }

    private void RecordFrameExportAttemptTelemetry(
        FrameExportEnvelope envelope,
        string sinkName,
        DateTimeOffset attemptedAtUtc,
        DateTimeOffset attemptedAtLocal,
        bool success,
        TimeSpan latency,
        string? errorMessage)
    {
        if (_telemetryRecorder is null)
        {
            return;
        }

        var latencyMilliseconds = Math.Max(0d, latency.TotalMilliseconds);
        var payloadBytes = envelope.Payload.Length;
        var metadata = envelope.Metadata;
        var payloadContentType = metadata.PayloadContentType ?? envelope.ContentType;
        var payloadExtension = metadata.PayloadExtension ?? envelope.FileExtension;

        _telemetryRecorder.RecordFrameExportAttempt(
            attemptedAtUtc,
            attemptedAtLocal,
            envelope.FrameId,
            envelope.Stage,
            sinkName,
            success,
            latencyMilliseconds,
            payloadBytes,
            payloadContentType,
            payloadExtension,
            metadata.QueueLatencyMilliseconds,
            metadata.ProcessingMilliseconds,
            metadata.FullPipelineMilliseconds,
            metadata.FramesStacked,
            metadata.IntegrationMilliseconds,
            errorMessage);
    }

    private async Task TryScheduleRetryAsync(FrameExportEnvelope envelope, string sinkName, int attemptCount, string? errorMessage, CancellationToken cancellationToken)
    {
        if (_retryQueue is null)
        {
            return;
        }

        try
        {
            await _retryQueue.ScheduleRetryAsync(new FrameExportRetryRequest(envelope, sinkName, attemptCount, errorMessage), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to schedule retry for frame {FrameId} via sink {Sink} (attempt {Attempt}).",
                envelope.FrameId,
                sinkName,
                attemptCount);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _concurrencyLimiter.Dispose();
    }
}
