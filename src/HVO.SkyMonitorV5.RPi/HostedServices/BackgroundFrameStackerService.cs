using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.HostedServices;

/// <summary>
/// Coordinates the background stacking pipeline. Phase 1 scaffolding creates the queueing infrastructure;
/// actual stacking logic will be layered on in subsequent phases.
/// </summary>
public sealed class BackgroundFrameStackerService : BackgroundService, IBackgroundFrameStacker, IDisposable
{
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger<BackgroundFrameStackerService> _logger;
    private readonly IFrameStacker _frameStacker;
    private readonly IFrameFilterPipeline _frameFilterPipeline;
    private readonly IFrameStateStore _frameStateStore;
    private readonly IFrameStackerConfigurationListener? _frameStackerConfigurationListener;
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private IDisposable? _optionsReloadSubscription;

    private Channel<StackingWorkItem> _channel = null!;
    private BackgroundStackerOptions _currentOptions = new();
    private int _lastConfigurationVersion = -1;
    private CameraConfiguration? _lastConfiguration;
    private int _queueDepth;
    private long _processedFrameCount;
    private long _droppedFrameCount;
    private double _totalQueueLatencyMs;
    private double _maxQueueLatencyMs;
    private double _totalStackMilliseconds;
    private double _totalFilterMilliseconds;
    private double _lastQueueLatencyMs;
    private double _lastStackMilliseconds;
    private double _lastFilterMilliseconds;
    private DateTimeOffset? _lastEnqueuedAt;
    private DateTimeOffset? _lastCompletedAt;
    private int? _lastProcessedFrameNumber;
    private int _queuePressureBucket = -1;
    private int _peakQueueDepth;
    private long _queueMemoryBytes;
    private long _maxQueueMemoryBytes;
    private readonly object _telemetryLock = new();
    private readonly Meter _meter;
    private readonly Counter<long> _processedFrameCounter;
    private readonly Counter<long> _droppedFrameCounter;
    private readonly Histogram<double> _queueLatencyHistogram;
    private readonly Histogram<double> _stackDurationHistogram;
    private readonly Histogram<double> _filterDurationHistogram;
    private readonly ObservableGauge<int> _queueDepthGauge;
    private readonly ObservableGauge<int> _queueCapacityGauge;
    private readonly ObservableGauge<int> _queuePeakDepthGauge;
    private readonly ObservableGauge<long> _queueMemoryGauge;
    private readonly ObservableGauge<long> _queuePeakMemoryGauge;
    private readonly ObservableGauge<double> _queueFillGauge;
    private readonly ObservableGauge<int> _queuePressureGauge;
    private readonly ObservableGauge<double> _secondsSinceLastFrameGauge;

    public BackgroundFrameStackerService(
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        IFrameStacker frameStacker,
        IFrameFilterPipeline frameFilterPipeline,
        IFrameStateStore frameStateStore,
        ILogger<BackgroundFrameStackerService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _frameStacker = frameStacker;
        _frameFilterPipeline = frameFilterPipeline;
        _frameStateStore = frameStateStore;
        _logger = logger;
        _frameStackerConfigurationListener = frameStacker as IFrameStackerConfigurationListener;

        _meter = new Meter("HVO.SkyMonitor.BackgroundStacker", "1.0.0");
        _processedFrameCounter = _meter.CreateCounter<long>(
            name: "hvo.skymonitor.background_stacker.frames_processed",
            unit: "frames",
            description: "Total number of frames processed by the background stacker.");
        _droppedFrameCounter = _meter.CreateCounter<long>(
            name: "hvo.skymonitor.background_stacker.frames_dropped",
            unit: "frames",
            description: "Total number of frames dropped by the background stacker.");
        _queueLatencyHistogram = _meter.CreateHistogram<double>(
            name: "hvo.skymonitor.background_stacker.queue_latency_ms",
            unit: "ms",
            description: "Latency between frame enqueue and processing start.");
        _stackDurationHistogram = _meter.CreateHistogram<double>(
            name: "hvo.skymonitor.background_stacker.stack_duration_ms",
            unit: "ms",
            description: "Duration of the stacking stage per frame.");
        _filterDurationHistogram = _meter.CreateHistogram<double>(
            name: "hvo.skymonitor.background_stacker.filter_duration_ms",
            unit: "ms",
            description: "Duration of the filter pipeline per frame.");
        _queueDepthGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.background_stacker.queue_depth",
            observeValue: ObserveQueueDepth,
            unit: "frames",
            description: "Current queue depth for the background stacker.");
        _queueCapacityGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.background_stacker.queue_capacity",
            observeValue: ObserveQueueCapacity,
            unit: "frames",
            description: "Configured queue capacity for the background stacker.");
        _queuePeakDepthGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.background_stacker.queue_peak_depth",
            observeValue: ObservePeakQueueDepth,
            unit: "frames",
            description: "Peak queue depth observed since the last reset.");
        _queueMemoryGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.background_stacker.queue_memory_bytes",
            observeValue: ObserveQueueMemoryBytes,
            unit: "bytes",
            description: "Estimated memory consumed by frames waiting in the queue.");
        _queuePeakMemoryGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.background_stacker.queue_peak_memory_bytes",
            observeValue: ObservePeakQueueMemoryBytes,
            unit: "bytes",
            description: "Peak memory consumption of queued frames since restart.");
        _queueFillGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.background_stacker.queue_fill_percent",
            observeValue: ObserveQueueFillPercentage,
            unit: "percent",
            description: "Current queue utilization percentage.");
        _queuePressureGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.background_stacker.queue_pressure_level",
            observeValue: ObserveQueuePressureLevel,
            unit: "level",
            description: "Queue pressure bucket (0-3) indicating utilization severity.");
        _secondsSinceLastFrameGauge = _meter.CreateObservableGauge(
            name: "hvo.skymonitor.background_stacker.seconds_since_last_completed",
            observeValue: ObserveSecondsSinceLastCompleted,
            unit: "s",
            description: "Seconds elapsed since the most recent frame completed processing.");

        _currentOptions = optionsMonitor.CurrentValue.BackgroundStacker ?? new BackgroundStackerOptions();
        _channel = CreateChannel(_currentOptions);
        _optionsReloadSubscription = optionsMonitor.OnChange(OnOptionsChanged);
        _lastConfigurationVersion = frameStateStore.ConfigurationVersion;
        _lastConfiguration = frameStateStore.Configuration;
        PublishStatus();
    }

    public bool IsEnabled => _currentOptions.Enabled;

    public override void Dispose()
    {
        base.Dispose();
        _channel.Writer.TryComplete();
        var drained = DrainPendingItems();
        if (drained > 0)
        {
            RecordDroppedFrames(drained, "service disposal");
        }

        PublishStatus();
        _optionsReloadSubscription?.Dispose();
        _meter.Dispose();
    }

    public ValueTask<bool> EnqueueAsync(StackingWorkItem workItem, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return ValueTask.FromResult(false);
        }

        return EnqueueInternalAsync(workItem, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background frame stacker service starting (enabled={Enabled}).", IsEnabled);
        using var _ = stoppingToken.Register(() => _logger.LogInformation("Background frame stacker service stopping..."));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWorkerAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ChannelClosedException ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Background stacker channel closed unexpectedly. Restarting loop after {Delay}s.", GetRestartDelay().TotalSeconds);
                await Task.Delay(GetRestartDelay(), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background frame stacker encountered an error. Restarting after {Delay}s.", GetRestartDelay().TotalSeconds);
                await Task.Delay(GetRestartDelay(), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Background frame stacker service stopped.");
    }

    private ValueTask<bool> EnqueueInternalAsync(StackingWorkItem workItem, CancellationToken cancellationToken) => EnqueueWithRetryAsync(workItem, cancellationToken);

    private async ValueTask<bool> EnqueueWithRetryAsync(StackingWorkItem workItem, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var channel = Volatile.Read(ref _channel);

            TryReduceQueueForOverflow(channel);

            if (await channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                if (channel.Writer.TryWrite(workItem))
                {
                    OnWorkItemEnqueued(workItem);
                    return true;
                }

                // Channel was swapped while waiting; retry with the new instance.
                continue;
            }

            break;
        }

        return false;
    }

    private static Channel<StackingWorkItem> CreateChannel(BackgroundStackerOptions options)
    {
        var capacity = Math.Max(1, options.QueueCapacity);
        var dropOldest = options.OverflowPolicy == BackgroundStackerOverflowPolicy.DropOldest;

        var boundedOptions = new BoundedChannelOptions(capacity)
        {
            SingleReader = !dropOldest,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        };

        return Channel.CreateBounded<StackingWorkItem>(boundedOptions);
    }

    private void OnOptionsChanged(CameraPipelineOptions options)
    {
        var newOptions = options.BackgroundStacker ?? new BackgroundStackerOptions();
        var previousOptions = _currentOptions;
        _currentOptions = newOptions;

        if (previousOptions.Enabled != newOptions.Enabled || previousOptions.QueueCapacity != newOptions.QueueCapacity || previousOptions.OverflowPolicy != newOptions.OverflowPolicy)
        {
            _logger.LogInformation(
                "Background stacker updated: Enabled={Enabled}, QueueCapacity={Capacity}, OverflowPolicy={Policy}, CompressionMode={CompressionMode}.",
                newOptions.Enabled,
                newOptions.QueueCapacity,
                newOptions.OverflowPolicy,
                newOptions.CompressionMode);

            var newChannel = CreateChannel(newOptions);
            var oldChannel = Interlocked.Exchange(ref _channel, newChannel);
            oldChannel.Writer.TryComplete();

            Interlocked.Exchange(ref _queuePressureBucket, -1);
            Interlocked.Exchange(ref _peakQueueDepth, Math.Max(0, Volatile.Read(ref _queueDepth)));
            UpdateQueuePressure(Math.Max(0, Volatile.Read(ref _queueDepth)));
            PublishStatus();
        }
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        var channel = Volatile.Read(ref _channel);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsEnabled)
            {
                var drained = DrainPendingItems();
                if (drained > 0)
                {
                    RecordDroppedFrames(drained, "stacker disabled");
                }

                UpdateQueuePressure(Math.Max(0, Volatile.Read(ref _queueDepth)));
                PublishStatus();

                await Task.Delay(DisabledPollInterval, stoppingToken).ConfigureAwait(false);
                channel = Volatile.Read(ref _channel);
                continue;
            }

            StackingWorkItem workItem;
            int queueDepthAfterDequeue;
            try
            {
                workItem = await channel.Reader.ReadAsync(stoppingToken).ConfigureAwait(false);
                queueDepthAfterDequeue = OnWorkItemDequeued(workItem);
            }
            catch (ChannelClosedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogDebug("Background stacker channel swapped or closed; restarting read loop.");
                channel = Volatile.Read(ref _channel);
                continue;
            }

            await ProcessWorkItemAsync(workItem, stoppingToken).ConfigureAwait(false);
            UpdateQueuePressure(queueDepthAfterDequeue);
        }
    }

    private void TryReduceQueueForOverflow(Channel<StackingWorkItem> channel)
    {
        if (_currentOptions.OverflowPolicy != BackgroundStackerOverflowPolicy.DropOldest)
        {
            return;
        }

        var capacity = Math.Max(1, _currentOptions.QueueCapacity);
        var depth = Volatile.Read(ref _queueDepth);

        while (depth >= capacity)
        {
            if (!channel.Reader.TryRead(out var droppedItem))
            {
                break;
            }

            var remaining = OnWorkItemDequeued(droppedItem);
            DisposeWorkItem(droppedItem);
            RecordDroppedFrames(1, "queue overflow (drop oldest)");
            UpdateQueuePressure(remaining);
            depth = remaining;
        }
    }

    private TimeSpan GetRestartDelay()
    {
        var seconds = Math.Clamp(_currentOptions.RestartDelaySeconds, 1, 600);
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task ProcessWorkItemAsync(StackingWorkItem workItem, CancellationToken stoppingToken)
    {
        var enqueueLatencyMs = (DateTimeOffset.UtcNow - workItem.EnqueuedAt).TotalMilliseconds;

        try
        {
            EnsureStackerConfiguration(workItem.ConfigurationVersion, workItem.ConfigurationSnapshot);

            FrameStackResult stackResult;
            double stackMilliseconds = 0;
            try
            {
                var stackStopwatch = Stopwatch.StartNew();
                stackResult = _frameStacker.Accumulate(workItem.Capture, workItem.ConfigurationSnapshot);
                stackStopwatch.Stop();
                stackMilliseconds = stackStopwatch.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stacker accumulate failed for frame #{FrameNumber}.", workItem.FrameNumber);
                _frameStateStore.SetLastError(ex);
                DisposeWorkItem(workItem);
                RecordDroppedFrames(1, "stack accumulate failure");
                return;
            }

            var frameContext = stackResult.Context;
            var filterStopwatch = Stopwatch.StartNew();
            var frameStored = false;
            double filterMilliseconds = 0;

            try
            {
                var processedFrame = await _frameFilterPipeline.ProcessAsync(stackResult, workItem.ConfigurationSnapshot, stoppingToken).ConfigureAwait(false);
                filterStopwatch.Stop();
                filterMilliseconds = filterStopwatch.Elapsed.TotalMilliseconds;

                processedFrame = processedFrame with
                {
                    ProcessingMilliseconds = (int)Math.Clamp(filterStopwatch.ElapsedMilliseconds, 0, int.MaxValue)
                };

                var rawSnapshot = new RawFrameSnapshot(stackResult.OriginalImage, stackResult.Timestamp, stackResult.Exposure);
                _frameStateStore.UpdateFrame(rawSnapshot, processedFrame);
                _frameStateStore.SetLastError(null);
                frameStored = true;

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Processed frame #{FrameNumber}: queue latency {LatencyMs:F1}ms, stack {StackMs:F1}ms, filters {FilterMs:F1}ms, frames stacked {FramesStacked}.",
                        workItem.FrameNumber,
                        enqueueLatencyMs,
                        stackMilliseconds,
                        filterMilliseconds,
                        processedFrame.FramesStacked);
                }

                if (!ReferenceEquals(stackResult.StackedImage, stackResult.OriginalImage))
                {
                    stackResult.StackedImage.Dispose();
                }

                RecordProcessingTelemetry(workItem, enqueueLatencyMs, stackMilliseconds, filterMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (!ReferenceEquals(stackResult.StackedImage, stackResult.OriginalImage))
                {
                    stackResult.StackedImage.Dispose();
                }

                if (!frameStored)
                {
                    stackResult.OriginalImage.Dispose();
                }

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process frame #{FrameNumber}.", workItem.FrameNumber);
                _frameStateStore.SetLastError(ex);

                if (!ReferenceEquals(stackResult.StackedImage, stackResult.OriginalImage))
                {
                    stackResult.StackedImage.Dispose();
                }

                if (!frameStored)
                {
                    stackResult.OriginalImage.Dispose();
                }

                RecordDroppedFrames(1, "filter pipeline failure");
            }
            finally
            {
                frameContext?.Dispose();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            DisposeWorkItem(workItem);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while processing frame #{FrameNumber}.", workItem.FrameNumber);
            _frameStateStore.SetLastError(ex);
            DisposeWorkItem(workItem);
            RecordDroppedFrames(1, "processing pipeline error");
        }
    }

    private void EnsureStackerConfiguration(int configurationVersion, CameraConfiguration configuration)
    {
        if (_lastConfigurationVersion == configurationVersion)
        {
            return;
        }

        var previousConfiguration = _lastConfiguration;

        if (previousConfiguration is null)
        {
            _frameStacker.Reset();
        }
        else if (_frameStackerConfigurationListener is not null)
        {
            _frameStackerConfigurationListener.OnConfigurationChanged(previousConfiguration, configuration);
        }
        else
        {
            _frameStacker.Reset();
        }

        _lastConfigurationVersion = configurationVersion;
        _lastConfiguration = configuration;
    }

    private int DrainPendingItems()
    {
        var channel = Volatile.Read(ref _channel);
        var drained = 0;

        while (channel.Reader.TryRead(out var pendingItem))
        {
            OnWorkItemDequeued(pendingItem);
            DisposeWorkItem(pendingItem);
            drained++;
        }

        if (drained > 0)
        {
            Interlocked.Exchange(ref _peakQueueDepth, Math.Max(Volatile.Read(ref _queueDepth), 0));
        }

        if (drained > 0 && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Discarded {Count} queued frames while draining pending items.", drained);
        }

        return drained;
    }

    private static void DisposeWorkItem(StackingWorkItem workItem, bool disposeImage = true)
    {
        if (disposeImage)
        {
            workItem.Capture.Image.Dispose();
        }

        workItem.Capture.Context?.Dispose();
    }

    private void OnWorkItemEnqueued(StackingWorkItem workItem)
    {
        var depth = IncrementQueueDepth();
        AddQueueMemory(workItem);
        UpdateQueuePressure(depth);
    }

    private int OnWorkItemDequeued(StackingWorkItem workItem)
    {
        var depth = Interlocked.Decrement(ref _queueDepth);
        if (depth < 0)
        {
            Interlocked.Exchange(ref _queueDepth, 0);
            depth = 0;
        }

        SubtractQueueMemory(workItem);
        return depth;
    }

    private int IncrementQueueDepth()
    {
        var depth = Interlocked.Increment(ref _queueDepth);
        var capacity = Math.Max(1, _currentOptions.QueueCapacity);

        if (depth > capacity)
        {
            Interlocked.Exchange(ref _queueDepth, capacity);
            depth = capacity;
        }

        UpdatePeakQueueDepth(depth);

        return depth;
    }

    private void UpdatePeakQueueDepth(int depth)
    {
        while (true)
        {
            var current = Volatile.Read(ref _peakQueueDepth);
            if (depth <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _peakQueueDepth, depth, current) == current)
            {
                return;
            }
        }
    }

    private void AddQueueMemory(StackingWorkItem workItem)
    {
        var bytes = GetWorkItemSizeInBytes(workItem);
        if (bytes <= 0)
        {
            return;
        }

        var total = Interlocked.Add(ref _queueMemoryBytes, bytes);
        UpdatePeakQueueMemory(total);
    }

    private void SubtractQueueMemory(StackingWorkItem workItem)
    {
        var bytes = GetWorkItemSizeInBytes(workItem);
        if (bytes <= 0)
        {
            return;
        }

        var total = Interlocked.Add(ref _queueMemoryBytes, -bytes);
        if (total < 0)
        {
            Interlocked.Exchange(ref _queueMemoryBytes, 0);
        }
    }

    private void UpdatePeakQueueMemory(long candidateBytes)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maxQueueMemoryBytes);
            if (candidateBytes <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _maxQueueMemoryBytes, candidateBytes, current) == current)
            {
                return;
            }
        }
    }

    private static long GetWorkItemSizeInBytes(StackingWorkItem workItem)
    {
        try
        {
            return workItem.Capture.Image?.Info.BytesSize ?? 0;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    private int ObserveQueueDepth()
    {
        var capacity = Math.Max(1, _currentOptions.QueueCapacity);
        return Math.Clamp(Volatile.Read(ref _queueDepth), 0, capacity);
    }

    private int ObserveQueueCapacity()
    {
        return Math.Max(1, _currentOptions.QueueCapacity);
    }

    private int ObservePeakQueueDepth()
    {
        var capacity = Math.Max(1, _currentOptions.QueueCapacity);
        return Math.Clamp(Volatile.Read(ref _peakQueueDepth), 0, capacity);
    }

    private long ObserveQueueMemoryBytes()
    {
        return Math.Max(0, Volatile.Read(ref _queueMemoryBytes));
    }

    private long ObservePeakQueueMemoryBytes()
    {
        return Math.Max(Math.Max(0, Volatile.Read(ref _queueMemoryBytes)), Volatile.Read(ref _maxQueueMemoryBytes));
    }

    private void RecordProcessingTelemetry(StackingWorkItem workItem, double queueLatencyMs, double stackMilliseconds, double filterMilliseconds)
    {
        BackgroundStackerStatus snapshot;

        lock (_telemetryLock)
        {
            _processedFrameCount++;
            _totalQueueLatencyMs += queueLatencyMs;
            _totalStackMilliseconds += stackMilliseconds;
            _totalFilterMilliseconds += filterMilliseconds;

            if (queueLatencyMs > _maxQueueLatencyMs)
            {
                _maxQueueLatencyMs = queueLatencyMs;
            }

            _lastQueueLatencyMs = queueLatencyMs;
            _lastStackMilliseconds = stackMilliseconds;
            _lastFilterMilliseconds = filterMilliseconds;
            _lastEnqueuedAt = workItem.EnqueuedAt;
            _lastCompletedAt = DateTimeOffset.UtcNow;
            _lastProcessedFrameNumber = workItem.FrameNumber;

            snapshot = BuildStatusSnapshotLocked();
        }

        _processedFrameCounter.Add(1);

        if (queueLatencyMs >= 0)
        {
            _queueLatencyHistogram.Record(queueLatencyMs);
        }

        if (stackMilliseconds >= 0)
        {
            _stackDurationHistogram.Record(stackMilliseconds);
        }

        if (filterMilliseconds >= 0)
        {
            _filterDurationHistogram.Record(filterMilliseconds);
        }

        _frameStateStore.UpdateBackgroundStackerStatus(snapshot);
    }

    private void RecordDroppedFrames(long count, string reason)
    {
        if (count <= 0)
        {
            return;
        }

        var total = Interlocked.Add(ref _droppedFrameCount, count);
        _droppedFrameCounter.Add(count);

        if (ShouldLogDropWarning(total))
        {
            _logger.LogWarning("Background stacker dropped {Count} frame(s) due to {Reason}. Total dropped: {Total}.", count, reason, total);
        }

        PublishStatus();
    }

    private static bool ShouldLogDropWarning(long total) => total == 1 || (total & (total - 1)) == 0;

    private void PublishStatus()
    {
        BackgroundStackerStatus snapshot;
        lock (_telemetryLock)
        {
            snapshot = BuildStatusSnapshotLocked();
        }

        _frameStateStore.UpdateBackgroundStackerStatus(snapshot);
    }

    private BackgroundStackerStatus BuildStatusSnapshotLocked()
    {
        var capacity = Math.Max(1, _currentOptions.QueueCapacity);
        var depth = Math.Clamp(Volatile.Read(ref _queueDepth), 0, capacity);
        var peakDepth = Math.Clamp(Volatile.Read(ref _peakQueueDepth), 0, capacity);
        var processed = _processedFrameCount;
        var dropped = Volatile.Read(ref _droppedFrameCount);
        var queueMemoryBytes = Math.Max(0, Volatile.Read(ref _queueMemoryBytes));
        var peakQueueMemoryBytes = Math.Max(queueMemoryBytes, Volatile.Read(ref _maxQueueMemoryBytes));

        double? avgQueueLatency = processed > 0 ? _totalQueueLatencyMs / processed : null;
        double? avgStackMs = processed > 0 ? _totalStackMilliseconds / processed : null;
        double? avgFilterMs = processed > 0 ? _totalFilterMilliseconds / processed : null;

        var queueFill = capacity == 0 ? 0d : (double)depth / capacity;
        var peakFill = capacity == 0 ? 0d : (double)peakDepth / capacity;
        var queueMemoryMegabytes = queueMemoryBytes / 1024d / 1024d;
        var peakQueueMemoryMegabytes = peakQueueMemoryBytes / 1024d / 1024d;
        double? secondsSinceLastCompleted = _lastCompletedAt is { } completed
            ? Math.Max(0d, (DateTimeOffset.UtcNow - completed).TotalSeconds)
            : null;
        var queuePressure = Math.Clamp(Volatile.Read(ref _queuePressureBucket), 0, 3);

        return new BackgroundStackerStatus(
            Enabled: IsEnabled,
            QueueDepth: depth,
            QueueCapacity: capacity,
            PeakQueueDepth: peakDepth,
            ProcessedFrameCount: processed,
            DroppedFrameCount: dropped,
            LastFrameNumber: _lastProcessedFrameNumber,
            LastEnqueuedAt: _lastEnqueuedAt,
            LastCompletedAt: _lastCompletedAt,
            LastQueueLatencyMilliseconds: _lastQueueLatencyMs > 0 ? _lastQueueLatencyMs : null,
            AverageQueueLatencyMilliseconds: avgQueueLatency,
            MaxQueueLatencyMilliseconds: _maxQueueLatencyMs > 0 ? _maxQueueLatencyMs : null,
            LastStackMilliseconds: _lastStackMilliseconds > 0 ? _lastStackMilliseconds : null,
            LastFilterMilliseconds: _lastFilterMilliseconds > 0 ? _lastFilterMilliseconds : null,
            AverageStackMilliseconds: avgStackMs,
            AverageFilterMilliseconds: avgFilterMs,
            QueueMemoryBytes: queueMemoryBytes,
            PeakQueueMemoryBytes: peakQueueMemoryBytes,
            QueueFillPercentage: Math.Clamp(queueFill * 100d, 0d, 100d),
            PeakQueueFillPercentage: Math.Clamp(peakFill * 100d, 0d, 100d),
            QueueMemoryMegabytes: double.IsFinite(queueMemoryMegabytes) ? queueMemoryMegabytes : 0d,
            PeakQueueMemoryMegabytes: double.IsFinite(peakQueueMemoryMegabytes) ? peakQueueMemoryMegabytes : 0d,
            SecondsSinceLastCompleted: secondsSinceLastCompleted,
            QueuePressureLevel: queuePressure);
    }

    private double ObserveQueueFillPercentage()
    {
        var capacity = Math.Max(1, _currentOptions.QueueCapacity);
        var depth = Math.Clamp(Volatile.Read(ref _queueDepth), 0, capacity);
        var fill = capacity == 0 ? 0d : (double)depth / capacity;
        return Math.Clamp(fill * 100d, 0d, 100d);
    }

    private int ObserveQueuePressureLevel()
    {
        var bucket = Volatile.Read(ref _queuePressureBucket);
        if (bucket < 0)
        {
            return 0;
        }

        return Math.Clamp(bucket, 0, 3);
    }

    private double ObserveSecondsSinceLastCompleted()
    {
        DateTimeOffset? lastCompleted;
        lock (_telemetryLock)
        {
            lastCompleted = _lastCompletedAt;
        }

        if (lastCompleted is null)
        {
            return 0d;
        }

        var seconds = (DateTimeOffset.UtcNow - lastCompleted.Value).TotalSeconds;
        return seconds < 0 ? 0d : seconds;
    }

    private void UpdateQueuePressure(int queueDepth)
    {
        var capacity = Math.Max(1, _currentOptions.QueueCapacity);
        queueDepth = Math.Clamp(queueDepth, 0, capacity);

        var bucket = CalculateQueuePressureBucket(queueDepth, capacity);
        var previous = Interlocked.Exchange(ref _queuePressureBucket, bucket);

        if (previous == bucket)
        {
            return;
        }

        var fillPercentage = capacity == 0 ? 0 : (double)queueDepth / capacity;

        if (bucket > previous)
        {
            switch (bucket)
            {
                case 3:
                    _logger.LogWarning("Background stacker queue pressure high: {Depth}/{Capacity} ({Fill:P0}).", queueDepth, capacity, fillPercentage);
                    break;
                case 2:
                    _logger.LogInformation("Background stacker queue pressure elevated: {Depth}/{Capacity} ({Fill:P0}).", queueDepth, capacity, fillPercentage);
                    break;
                case 1:
                    _logger.LogDebug("Background stacker queue pressure rising: {Depth}/{Capacity} ({Fill:P0}).", queueDepth, capacity, fillPercentage);
                    break;
                default:
                    _logger.LogTrace("Background stacker queue depth updated: {Depth}/{Capacity} ({Fill:P0}).", queueDepth, capacity, fillPercentage);
                    break;
            }
        }
        else
        {
            _logger.LogDebug("Background stacker queue pressure easing: {Depth}/{Capacity} ({Fill:P0}).", queueDepth, capacity, fillPercentage);
        }
    }

    private static int CalculateQueuePressureBucket(int depth, int capacity)
    {
        if (capacity <= 0)
        {
            return 0;
        }

        var fill = (double)depth / capacity;
        if (fill >= 0.9)
        {
            return 3;
        }

        if (fill >= 0.75)
        {
            return 2;
        }

        if (fill >= 0.5)
        {
            return 1;
        }

        return 0;
    }
}
