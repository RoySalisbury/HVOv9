using System;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Services.RemoteDispatch;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Threading.Channels;

namespace HVO.SkyMonitorV5.RPi.HostedServices;

public sealed class AllSkyCaptureService : BackgroundService
{
    private readonly ILogger<AllSkyCaptureService> _logger;
    private readonly IRigAcquisitionAdapter _rigAdapter;
    private readonly IExposureController _exposureController;
    private readonly IExposureAnalyzer? _exposureAnalyzer;
    private readonly IFrameStacker _frameStacker;
    private readonly IFrameStackerConfigurationListener? _frameStackerConfigurationListener;
    private readonly IFrameFilterPipeline _frameFilterPipeline;
    private readonly IFrameStateStore _frameStateStore;
    private readonly IBackgroundFrameStacker _backgroundFrameStacker;
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private readonly IObservatoryClock _clock;
    private readonly IRemoteFramePublisher _remoteFramePublisher;
    private RigSpec? _lastPublishedRig;

    private double _dynamicCaptureDelayMilliseconds = MinimumFrameDelayMilliseconds;
    private int _lastCapturePacingBucket = -1;
    private DateTimeOffset? _captureRejectionPenaltyUntil;
    private readonly object _processingQueueSync = new();
    private readonly ProcessingQueueMetrics _processingQueueMetrics = new();
    private int _processingQueueCapacity;
    private int _processingQueueDepth;

    private const int MinimumFrameDelayMilliseconds = 250;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private int _frameNumber;

    public AllSkyCaptureService(
        ILogger<AllSkyCaptureService> logger,
        IRigAcquisitionAdapter rigAdapter,
    IExposureController exposureController,
    IExposureAnalyzer? exposureAnalyzer,
        IFrameStacker frameStacker,
        IFrameFilterPipeline frameFilterPipeline,
        IFrameStateStore frameStateStore,
        IBackgroundFrameStacker backgroundFrameStacker,
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
    IRemoteFramePublisher remoteFramePublisher,
    IObservatoryClock clock)
    {
        _logger = logger;
        _rigAdapter = rigAdapter ?? throw new ArgumentNullException(nameof(rigAdapter));
    _exposureController = exposureController;
    _exposureAnalyzer = exposureAnalyzer;
        _frameStacker = frameStacker;
        _frameStackerConfigurationListener = frameStacker as IFrameStackerConfigurationListener;
        _frameFilterPipeline = frameFilterPipeline;
        _frameStateStore = frameStateStore;
        _backgroundFrameStacker = backgroundFrameStacker;
        _optionsMonitor = optionsMonitor;
    _remoteFramePublisher = remoteFramePublisher ?? throw new ArgumentNullException(nameof(remoteFramePublisher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SkyMonitor capture service starting.");
        UpdateRigState();

        while (!stoppingToken.IsCancellationRequested)
        {
            var rigStart = await _rigAdapter.StartAsync(stoppingToken);
            if (rigStart.IsFailure)
            {
                var rigError = rigStart.Error ?? new InvalidOperationException("Rig acquisition adapter failed to start.");
                _frameStateStore.SetLastError(rigError);
                _logger.LogError(rigError, "Unable to start rig acquisition adapter. Retrying in {DelaySeconds}s", RetryDelay.TotalSeconds);
                await DelayWithCancellation(RetryDelay, stoppingToken);
                continue;
            }
            try
            {
                _frameStateStore.UpdateRunningState(true);
                UpdateRigState();
                await RunCaptureLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown requested
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in capture loop. Rig will be restarted.");
                _frameStateStore.SetLastError(ex);
                await DelayWithCancellation(RetryDelay, stoppingToken);
            }
            finally
            {
                _frameStateStore.UpdateRunningState(false);
                _frameStateStore.SetLastError(null);
                var stopResult = await _rigAdapter.StopAsync(CancellationToken.None);
                if (stopResult.IsFailure)
                {
                    _logger.LogWarning(stopResult.Error, "Rig adapter stop reported an error.");
                }
            }
        }

        _logger.LogInformation("SkyMonitor capture service stopping.");
    }

    private async Task RunCaptureLoopAsync(CancellationToken stoppingToken)
    {
        var configurationVersion = _frameStateStore.ConfigurationVersion;
        var configuration = _frameStateStore.Configuration;
        var useAsyncProcessing = _optionsMonitor.CurrentValue.EnableAsyncProcessing;
        Channel<ProcessingWorkItem>? processingChannel = null;
        Task? processorTask = null;

        if (useAsyncProcessing)
        {
            var channelOptions = new BoundedChannelOptions(2)
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            };
            processingChannel = Channel.CreateBounded<ProcessingWorkItem>(channelOptions);
            ResetProcessingQueueMetrics(channelOptions.Capacity, enabled: true);
            processorTask = ProcessWorkItemsAsync(processingChannel.Reader, stoppingToken);
        }
        else
        {
            ResetProcessingQueueMetrics(0, enabled: false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            UpdateRigState();
            var usingBackgroundStacker = _backgroundFrameStacker.IsEnabled;
            configurationVersion = CheckForConfigurationUpdates(configurationVersion, ref configuration, usingBackgroundStacker);

            var frameStopwatch = Stopwatch.StartNew();
            double captureMs = 0;

            var exposure = _exposureController.CreateNextExposure(configuration);
            _logger.LogTrace("Prepared exposure {ExposureMs}ms / Gain {Gain}", exposure.ExposureMilliseconds, exposure.Gain);

            var captureStopwatch = Stopwatch.StartNew();
            var captureResult = await _rigAdapter.CaptureAsync(exposure, stoppingToken);
            captureStopwatch.Stop();
            captureMs = captureStopwatch.Elapsed.TotalMilliseconds;
            if (captureResult.IsFailure)
            {
                frameStopwatch.Stop();
                await HandleCaptureFailureAsync(captureResult.Error, stoppingToken);
                continue;
            }

            var capturedFrame = captureResult.Value;
            var frameNumber = ++_frameNumber;
            var capturedAtLocal = _clock.ToLocal(capturedFrame.Timestamp);

            PerformExposureAnalysis(capturedFrame, configuration, frameNumber);

            await DispatchRemoteAsync(
                frameNumber,
                capturedFrame,
                configuration,
                configurationVersion,
                usingBackgroundStacker,
                captureMs,
                capturedAtLocal,
                stoppingToken).ConfigureAwait(false);

            if (useAsyncProcessing)
            {
                double enqueueWaitMs = 0;
                var workItem = new ProcessingWorkItem(
                    frameNumber,
                    capturedFrame,
                    configuration,
                    configurationVersion,
                    usingBackgroundStacker,
                    captureMs,
                    frameStopwatch,
                    capturedAtLocal);

                try
                {
                    var enqueueStopwatch = Stopwatch.StartNew();
                    await processingChannel!.Writer.WriteAsync(workItem, stoppingToken).ConfigureAwait(false);
                    enqueueStopwatch.Stop();
                    enqueueWaitMs = enqueueStopwatch.Elapsed.TotalMilliseconds;
                    RecordProcessingQueueEnqueue(enqueueWaitMs);
                }
                catch (ChannelClosedException)
                {
                    frameStopwatch.Stop();
                    _logger.LogWarning("Processing channel closed while queuing frame #{FrameNumber}.", frameNumber);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    frameStopwatch.Stop();
                    break;
                }

                var captureInterval = _optionsMonitor.CurrentValue.CaptureIntervalMilliseconds;
                var remainingMs = captureInterval - (int)Math.Round(captureMs);
                var delayMs = Math.Max(remainingMs, 0);
                if (delayMs < MinimumFrameDelayMilliseconds)
                {
                    delayMs = MinimumFrameDelayMilliseconds;
                }

                delayMs = ApplyCapturePacing(delayMs, usingBackgroundStacker);

                _logger.LogDebug(
                    "Captured frame #{FrameNumber} queued for async processing at {TimestampLocal} (capture {CaptureMs:F1}ms, queue wait {QueueWaitMs:F1}ms). Next capture in {Delay}ms.",
                    frameNumber,
                    workItem.CapturedAtLocal,
                    captureMs,
                    enqueueWaitMs,
                    delayMs);

                await DelayWithCancellation(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
            }
            else
            {
                var processingResult = await ProcessCapturedFrameAsync(
                    frameNumber,
                    capturedFrame,
                    configuration,
                    configurationVersion,
                    usingBackgroundStacker,
                    stoppingToken).ConfigureAwait(false);

                frameStopwatch.Stop();
                var totalMs = frameStopwatch.Elapsed.TotalMilliseconds;

                var captureInterval = _optionsMonitor.CurrentValue.CaptureIntervalMilliseconds;
                var remainingMs = captureInterval - (int)Math.Round(totalMs);
                var delayMs = Math.Max(remainingMs, 0);
                if (delayMs < MinimumFrameDelayMilliseconds)
                {
                    delayMs = MinimumFrameDelayMilliseconds;
                }

                delayMs = ApplyCapturePacing(delayMs, usingBackgroundStacker);

                if (processingResult.UsingBackgroundStacker && processingResult.Enqueued)
                {
                    _logger.LogDebug(
                        "Captured frame #{FrameNumber} at {TimestampLocal} (capture {CaptureMs:F1}ms, enqueue {EnqueueMs:F1}ms, total {TotalMs:F1}ms). Next capture in {Delay}ms.",
                        frameNumber,
                        processingResult.CapturedAtLocal,
                        captureMs,
                        processingResult.EnqueueMilliseconds,
                        totalMs,
                        delayMs);
                }
                else
                {
                    _logger.LogDebug(
                        "Captured frame #{FrameNumber} at {TimestampLocal} (capture {CaptureMs:F1}ms, stack {StackMs:F1}ms, filters {FilterMs:F1}ms, total {TotalMs:F1}ms). Next capture in {Delay}ms.",
                        frameNumber,
                        processingResult.CapturedAtLocal,
                        captureMs,
                        processingResult.StackMilliseconds,
                        processingResult.FilterMilliseconds,
                        totalMs,
                        delayMs);
                }

                await DelayWithCancellation(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
            }
        }

        if (useAsyncProcessing && processingChannel is not null)
        {
            processingChannel.Writer.TryComplete();
            try
            {
                if (processorTask is not null)
                {
                    await processorTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore cancellation during shutdown
            }

            ResetProcessingQueueMetrics(0, enabled: false);
        }
    }

    private async Task DispatchRemoteAsync(
        int frameNumber,
        CapturedImage capturedFrame,
        CameraConfiguration configuration,
        int configurationVersion,
        bool usingBackgroundStacker,
        double captureMilliseconds,
        DateTimeOffset capturedAtLocal,
        CancellationToken cancellationToken)
    {
        RemoteDispatchResult result;

        try
        {
            var envelope = new RemoteFrameEnvelope(
                frameNumber,
                capturedFrame,
                _rigAdapter.ActiveRig,
                configuration,
                configurationVersion,
                usingBackgroundStacker,
                captureMilliseconds,
                capturedAtLocal,
                capturedFrame.Timestamp);

            result = await _remoteFramePublisher.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remote dispatch failed for frame #{FrameNumber}.", frameNumber);
            result = RemoteDispatchResult.Failure("Unknown", "Remote dispatch threw an exception.", ex);
        }

        LogRemoteDispatchResult(frameNumber, result);
        UpdateRemoteDispatchStatus(result, capturedAtLocal);
    }

    private void LogRemoteDispatchResult(int frameNumber, RemoteDispatchResult result)
    {
        switch (result.Outcome)
        {
            case RemoteDispatchOutcome.Succeeded:
                _logger.LogDebug(
                    "Remote dispatch succeeded for frame #{FrameNumber} via mode {Mode}. {Message}",
                    frameNumber,
                    result.Mode,
                    result.Message);
                break;
            case RemoteDispatchOutcome.Disabled:
            case RemoteDispatchOutcome.Skipped:
                _logger.LogTrace(
                    "Remote dispatch skipped for frame #{FrameNumber} (mode: {Mode}, reason: {Reason}).",
                    frameNumber,
                    result.Mode,
                    result.Message ?? "n/a");
                break;
            case RemoteDispatchOutcome.Failed:
                _logger.LogWarning(
                    result.Error,
                    "Remote dispatch failed for frame #{FrameNumber} (mode: {Mode}): {Reason}",
                    frameNumber,
                    result.Mode,
                    result.Message ?? "Unknown failure");
                break;
        }
    }

    private void UpdateRemoteDispatchStatus(RemoteDispatchResult result, DateTimeOffset capturedAtLocal)
    {
        var status = new RemoteDispatchStatus(
            Timestamp: _clock.UtcNow,
            Mode: result.Mode,
            Outcome: result.Outcome,
            CapturedAtLocal: capturedAtLocal,
            Message: result.Message,
            ErrorMessage: result.Error?.Message);

        var metrics = result.Metrics ?? RemoteDispatchEventMetrics.Empty;
        _frameStateStore.UpdateRemoteDispatchStatus(status, metrics);
    }

    private async Task<(double StackMilliseconds, double FilterMilliseconds)> ProcessFrameSynchronouslyAsync(
        CapturedImage capturedFrame,
        CameraConfiguration configuration,
        CancellationToken stoppingToken)
    {
        var stackStopwatch = Stopwatch.StartNew();
        var stackResult = _frameStacker.Accumulate(capturedFrame, configuration);
        stackStopwatch.Stop();
        var stackMs = stackStopwatch.Elapsed.TotalMilliseconds;

        var frameStored = false;

        try
        {
            var filterStopwatch = Stopwatch.StartNew();
            var processedFrame = await _frameFilterPipeline.ProcessAsync(stackResult, configuration, stoppingToken);
            filterStopwatch.Stop();
            var filterMs = filterStopwatch.Elapsed.TotalMilliseconds;

            processedFrame = processedFrame with
            {
                ProcessingMilliseconds = (int)Math.Clamp(filterStopwatch.ElapsedMilliseconds, 0, int.MaxValue)
            };

            _frameStateStore.UpdateFrame(
                new RawFrameSnapshot(stackResult.OriginalImage, stackResult.Timestamp, stackResult.Exposure),
                processedFrame);
            _frameStateStore.SetLastError(null);
            frameStored = true;

            if (!ReferenceEquals(stackResult.StackedImage, stackResult.OriginalImage))
            {
                stackResult.StackedImage.Dispose();
            }

            return (stackMs, filterMs);
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
            _logger.LogError(ex, "Failed to process frame synchronously.");
            _frameStateStore.SetLastError(ex);

            if (!ReferenceEquals(stackResult.StackedImage, stackResult.OriginalImage))
            {
                stackResult.StackedImage.Dispose();
            }

            if (!frameStored)
            {
                stackResult.OriginalImage.Dispose();
            }

            return (stackMs, 0);
        }
    }

    private void UpdateRigState()
    {
        var activeRig = _rigAdapter.ActiveRig;
        if (_lastPublishedRig is not null && string.Equals(_lastPublishedRig.Name, activeRig.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _frameStateStore.UpdateRig(activeRig);
        _lastPublishedRig = activeRig;
        _logger.LogInformation("Active rig set to {RigName}.", activeRig.Name);
    }

    private int CheckForConfigurationUpdates(int currentVersion, ref CameraConfiguration configuration, bool usingBackgroundStacker)
    {
        var latestVersion = _frameStateStore.ConfigurationVersion;
        if (latestVersion != currentVersion)
        {
            var previousConfiguration = configuration;
            configuration = _frameStateStore.Configuration;

            if (!usingBackgroundStacker)
            {
                if (_frameStackerConfigurationListener is null)
                {
                    _frameStacker.Reset();
                    _logger.LogInformation("Camera configuration updated. Frame stacker has been reset.");
                }
                else
                {
                    _frameStackerConfigurationListener.OnConfigurationChanged(previousConfiguration, configuration);
                    _logger.LogInformation("Camera configuration updated. Frame stacker configuration listener invoked.");
                }
            }
            else
            {
                _logger.LogInformation("Camera configuration updated. Background stacker will apply the new settings on the next frame.");
            }
            return latestVersion;
        }

        return currentVersion;
    }

    private async Task HandleCaptureFailureAsync(Exception? exception, CancellationToken stoppingToken)
    {
        var error = exception ?? new InvalidOperationException("Camera capture failed without an exception instance.");
        _frameStateStore.SetLastError(error);
        _logger.LogError(error, "Rig capture failed. Retrying after short delay.");

        var delay = TimeSpan.FromSeconds(2);
        await DelayWithCancellation(delay, stoppingToken);
    }

    private static async Task DelayWithCancellation(TimeSpan delay, CancellationToken stoppingToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // ignore cancellation - caller will respect token
        }
    }

    private async Task<ProcessingResult> ProcessCapturedFrameAsync(
        int frameNumber,
        CapturedImage capturedFrame,
        CameraConfiguration configuration,
        int configurationVersion,
        bool usingBackgroundStacker,
        CancellationToken stoppingToken)
    {
        var capturedAtLocal = _clock.ToLocal(capturedFrame.Timestamp);
        var enqueued = false;
        double stackMs = 0;
        double filterMs = 0;
        double enqueueMs = 0;

        if (usingBackgroundStacker)
        {
            var captureSizeBytes = 0L;
            var image = capturedFrame.Image;
            if (image is not null)
            {
                try
                {
                    captureSizeBytes = image.Info.BytesSize;
                }
                catch (ObjectDisposedException)
                {
                    captureSizeBytes = 0;
                }
            }

            var workItem = new StackingWorkItem(
                frameNumber,
                capturedFrame,
                configuration,
                configurationVersion,
                DateTimeOffset.UtcNow,
                captureSizeBytes);
            var enqueueStopwatch = Stopwatch.StartNew();
            enqueued = await _backgroundFrameStacker.EnqueueAsync(workItem, stoppingToken).ConfigureAwait(false);
            enqueueStopwatch.Stop();
            enqueueMs = enqueueStopwatch.Elapsed.TotalMilliseconds;

            if (!enqueued)
            {
                _logger.LogWarning(
                    "Background stacker rejected frame #{FrameNumber}; falling back to synchronous processing.",
                    frameNumber);
                RecordCaptureRejectionPenalty();
            }
        }

        if (!usingBackgroundStacker || !enqueued)
        {
            var (stack, filter) = await ProcessFrameSynchronouslyAsync(capturedFrame, configuration, stoppingToken).ConfigureAwait(false);
            stackMs = stack;
            filterMs = filter;
        }

        return new ProcessingResult(usingBackgroundStacker, enqueued, stackMs, filterMs, enqueueMs, capturedAtLocal);
    }

    private int ApplyCapturePacing(int baseDelayMilliseconds, bool usingBackgroundStacker)
    {
        if (baseDelayMilliseconds < MinimumFrameDelayMilliseconds)
        {
            baseDelayMilliseconds = MinimumFrameDelayMilliseconds;
        }

        var pacing = _optionsMonitor.CurrentValue.CapturePacing;
        if (pacing is null)
        {
            return ResetCapturePacing(baseDelayMilliseconds, usingBackgroundStacker, null);
        }

        pacing.Normalize();

        if (!pacing.Enabled || !usingBackgroundStacker)
        {
            return ResetCapturePacing(baseDelayMilliseconds, usingBackgroundStacker, pacing);
        }

        var status = _frameStateStore.BackgroundStackerStatus;
        var bucket = status?.QueuePressureLevel ?? 0;
        bucket = Math.Clamp(bucket, 0, 3);

        var now = _clock.UtcNow;
        var penaltyActive = TryGetActiveRejectionPenalty(now, out var penaltyUntilUtc);

        var additionalDelay = bucket switch
        {
            3 => pacing.CriticalAdditionalDelayMilliseconds,
            2 => pacing.HighAdditionalDelayMilliseconds,
            1 => pacing.ElevatedAdditionalDelayMilliseconds,
            _ => 0
        };

        if (penaltyActive)
        {
            bucket = Math.Max(bucket, 3);
        }

        var penaltyDelay = penaltyActive ? pacing.RejectionPenaltyMilliseconds : 0;

        var desiredDelay = baseDelayMilliseconds + additionalDelay;
        if (penaltyDelay > 0)
        {
            desiredDelay = Math.Max(desiredDelay, baseDelayMilliseconds + penaltyDelay);
        }

        if (_dynamicCaptureDelayMilliseconds < MinimumFrameDelayMilliseconds)
        {
            _dynamicCaptureDelayMilliseconds = baseDelayMilliseconds;
        }

        if (_dynamicCaptureDelayMilliseconds < desiredDelay)
        {
            _dynamicCaptureDelayMilliseconds = Math.Min(desiredDelay, _dynamicCaptureDelayMilliseconds + pacing.RampUpStepMilliseconds);
        }
        else if (_dynamicCaptureDelayMilliseconds > desiredDelay)
        {
            _dynamicCaptureDelayMilliseconds = Math.Max(desiredDelay, _dynamicCaptureDelayMilliseconds - pacing.RampDownStepMilliseconds);
        }
        else
        {
            _dynamicCaptureDelayMilliseconds = desiredDelay;
        }

        var adjustedDelay = (int)Math.Clamp(_dynamicCaptureDelayMilliseconds, MinimumFrameDelayMilliseconds, pacing.MaxDelayMilliseconds);

        if (bucket != _lastCapturePacingBucket)
        {
            LogCapturePacingChange(bucket, adjustedDelay, baseDelayMilliseconds, additionalDelay, penaltyDelay);
            _lastCapturePacingBucket = bucket;
        }

        PublishCapturePacingStatus(
            pacingEnabled: pacing.Enabled,
            usingBackgroundStacker: usingBackgroundStacker,
            baseDelayMilliseconds: baseDelayMilliseconds,
            adjustedDelayMilliseconds: adjustedDelay,
            bucket: bucket,
            pressureAdditionalDelayMilliseconds: additionalDelay,
            penaltyAdditionalDelayMilliseconds: penaltyDelay,
            penaltyActive: penaltyActive,
            penaltyExpiresUtc: penaltyUntilUtc);

        return adjustedDelay;
    }

    private int ResetCapturePacing(int baseDelayMilliseconds, bool usingBackgroundStacker, CapturePacingOptions? pacingOptions)
    {
        if (_lastCapturePacingBucket > 0)
        {
            _logger.LogDebug(
                "Capture pacing reset to baseline delay {Delay}ms after pressure normalized.",
                baseDelayMilliseconds);
        }

        _lastCapturePacingBucket = 0;
        _dynamicCaptureDelayMilliseconds = baseDelayMilliseconds;
        _captureRejectionPenaltyUntil = null;

        PublishCapturePacingStatus(
            pacingEnabled: pacingOptions?.Enabled ?? false,
            usingBackgroundStacker: usingBackgroundStacker,
            baseDelayMilliseconds: baseDelayMilliseconds,
            adjustedDelayMilliseconds: baseDelayMilliseconds,
            bucket: 0,
            pressureAdditionalDelayMilliseconds: 0,
            penaltyAdditionalDelayMilliseconds: 0,
            penaltyActive: false,
            penaltyExpiresUtc: null);

        return baseDelayMilliseconds;
    }

    private void LogCapturePacingChange(int bucket, int adjustedDelay, int baseDelay, int additionalDelay, int penaltyDelay)
    {
        if (bucket <= 0)
        {
            _logger.LogDebug(
                "Capture pacing easing to baseline delay {Delay}ms (base {BaseDelay}ms).",
                adjustedDelay,
                baseDelay);
            return;
        }

        var severity = bucket switch
        {
            3 => "critical",
            2 => "high",
            _ => "elevated"
        };

        if (penaltyDelay > 0)
        {
            _logger.LogWarning(
                "Capture pacing {Severity} pressure + rejection penalty; applying {AdjustedDelay}ms delay (base {BaseDelay}ms, pressure {AdditionalDelay}ms, penalty {PenaltyDelay}ms).",
                severity,
                adjustedDelay,
                baseDelay,
                additionalDelay,
                penaltyDelay);
        }
        else
        {
            _logger.LogInformation(
                "Capture pacing {Severity} pressure; applying {AdjustedDelay}ms delay (base {BaseDelay}ms, additional {AdditionalDelay}ms).",
                severity,
                adjustedDelay,
                baseDelay,
                additionalDelay);
        }
    }

    private bool TryGetActiveRejectionPenalty(DateTimeOffset now, out DateTimeOffset? penaltyExpiresUtc)
    {
        var until = _captureRejectionPenaltyUntil;
        if (until is null)
        {
            penaltyExpiresUtc = null;
            return false;
        }

        if (now >= until)
        {
            _captureRejectionPenaltyUntil = null;
            penaltyExpiresUtc = null;
            return false;
        }

        penaltyExpiresUtc = until;
        return true;
    }

    private void PublishCapturePacingStatus(
        bool pacingEnabled,
        bool usingBackgroundStacker,
        int baseDelayMilliseconds,
        int adjustedDelayMilliseconds,
        int bucket,
        int pressureAdditionalDelayMilliseconds,
        int penaltyAdditionalDelayMilliseconds,
        bool penaltyActive,
        DateTimeOffset? penaltyExpiresUtc)
    {
        var timestampUtc = _clock.UtcNow;

        var status = new CapturePacingStatus(
            Timestamp: timestampUtc,
            Enabled: pacingEnabled,
            UsingBackgroundStacker: usingBackgroundStacker,
            BaseDelayMilliseconds: baseDelayMilliseconds,
            AdjustedDelayMilliseconds: adjustedDelayMilliseconds,
            QueuePressureLevel: Math.Clamp(bucket, 0, 3),
            PressureAdditionalDelayMilliseconds: pressureAdditionalDelayMilliseconds,
            PenaltyAdditionalDelayMilliseconds: penaltyAdditionalDelayMilliseconds,
            PenaltyActive: penaltyActive,
            PenaltyExpiresAt: penaltyExpiresUtc);

        _frameStateStore.UpdateCapturePacingStatus(status);
    }

    private void RecordCaptureRejectionPenalty()
    {
        var pacing = _optionsMonitor.CurrentValue.CapturePacing;
        if (pacing is null)
        {
            return;
        }

        pacing.Normalize();

        if (!pacing.Enabled)
        {
            return;
        }

        if (pacing.RejectionPenaltyMilliseconds <= 0)
        {
            return;
        }

        var now = _clock.UtcNow;

        if (pacing.RejectionPenaltyDurationSeconds > 0)
        {
            _captureRejectionPenaltyUntil = now.AddSeconds(pacing.RejectionPenaltyDurationSeconds);
        }
        else
        {
            _captureRejectionPenaltyUntil = null;
        }

        _dynamicCaptureDelayMilliseconds = Math.Max(
            _dynamicCaptureDelayMilliseconds,
            MinimumFrameDelayMilliseconds + pacing.RejectionPenaltyMilliseconds);

        _logger.LogWarning(
            "Capture pacing rejection penalty applied: +{PenaltyDelay}ms for {Duration}s after queue rejection.",
            pacing.RejectionPenaltyMilliseconds,
            pacing.RejectionPenaltyDurationSeconds);
    }

    private void ResetProcessingQueueMetrics(int capacity, bool enabled)
    {
        lock (_processingQueueSync)
        {
            _processingQueueCapacity = capacity;
            _processingQueueDepth = 0;
            _processingQueueMetrics.Reset(enabled);

            if (!enabled)
            {
                _frameStateStore.UpdateProcessingQueueStatus(ProcessingQueueStatus.Disabled(_clock.UtcNow));
            }
            else
            {
                PublishProcessingQueueStatusLocked();
            }
        }
    }

    private void RecordProcessingQueueEnqueue(double waitMilliseconds)
    {
        lock (_processingQueueSync)
        {
            if (!_processingQueueMetrics.Enabled)
            {
                return;
            }

            if (_processingQueueCapacity > 0)
            {
                _processingQueueDepth = Math.Min(_processingQueueCapacity, _processingQueueDepth + 1);
            }

            _processingQueueMetrics.RecordEnqueue(waitMilliseconds);
            PublishProcessingQueueStatusLocked();
        }
    }

    private void RecordProcessingQueueDequeue()
    {
        lock (_processingQueueSync)
        {
            if (!_processingQueueMetrics.Enabled)
            {
                return;
            }

            if (_processingQueueDepth > 0)
            {
                _processingQueueDepth--;
            }

            PublishProcessingQueueStatusLocked();
        }
    }

    private void RecordProcessingQueueMetrics(double processingMilliseconds)
    {
        lock (_processingQueueSync)
        {
            if (!_processingQueueMetrics.Enabled)
            {
                return;
            }

            _processingQueueMetrics.RecordProcessing(processingMilliseconds);
            PublishProcessingQueueStatusLocked();
        }
    }

    private void PublishProcessingQueueStatusLocked()
    {
        var status = _processingQueueMetrics.CreateStatus(_clock.UtcNow, _processingQueueDepth, _processingQueueCapacity);
        _frameStateStore.UpdateProcessingQueueStatus(status);
    }

    private void PerformExposureAnalysis(CapturedImage capturedFrame, CameraConfiguration configuration, int frameNumber)
    {
        if (_exposureAnalyzer is null)
        {
            return;
        }

        try
        {
            var analysis = _exposureAnalyzer.Analyze(capturedFrame, configuration);
            _exposureController.ApplyAnalysis(analysis);
            _frameStateStore.UpdateExposureAnalysis(analysis, capturedFrame.Timestamp);

            if (analysis.SuggestedExposure is { } suggested)
            {
                _logger.LogDebug(
                    "Exposure analysis for frame #{FrameNumber} recommended {ExposureMs}ms / gain {Gain} (avg luminance {AverageLuminance:F1}, lighting {Lighting}, notes: {Notes}).",
                    frameNumber,
                    suggested.ExposureMilliseconds,
                    suggested.Gain,
                    analysis.Metrics.AverageLuminance,
                    analysis.LightingCondition,
                    analysis.Notes ?? "n/a");
            }
            else
            {
                _logger.LogTrace(
                    "Exposure analysis for frame #{FrameNumber} recorded average luminance {AverageLuminance:F1} ({Lighting}).",
                    frameNumber,
                    analysis.Metrics.AverageLuminance,
                    analysis.LightingCondition);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exposure analysis failed for frame #{FrameNumber}.", frameNumber);
        }
    }

    private async Task ProcessWorkItemsAsync(ChannelReader<ProcessingWorkItem> reader, CancellationToken cancellationToken)
    {
        await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            RecordProcessingQueueDequeue();
            try
            {
                var result = await ProcessCapturedFrameAsync(
                    item.FrameNumber,
                    item.CapturedFrame,
                    item.Configuration,
                    item.ConfigurationVersion,
                    item.UsingBackgroundStacker,
                    cancellationToken).ConfigureAwait(false);

                item.FrameStopwatch.Stop();
                var totalMs = item.FrameStopwatch.Elapsed.TotalMilliseconds;

                if (result.UsingBackgroundStacker && result.Enqueued)
                {
                    _logger.LogDebug(
                        "Processed frame #{FrameNumber} at {TimestampLocal} (capture {CaptureMs:F1}ms, enqueue {EnqueueMs:F1}ms, total {TotalMs:F1}ms).",
                        item.FrameNumber,
                        result.CapturedAtLocal,
                        item.CaptureMilliseconds,
                        result.EnqueueMilliseconds,
                        totalMs);
                }
                else
                {
                    _logger.LogDebug(
                        "Processed frame #{FrameNumber} at {TimestampLocal} (capture {CaptureMs:F1}ms, stack {StackMs:F1}ms, filters {FilterMs:F1}ms, total {TotalMs:F1}ms).",
                        item.FrameNumber,
                        result.CapturedAtLocal,
                        item.CaptureMilliseconds,
                        result.StackMilliseconds,
                        result.FilterMilliseconds,
                        totalMs);
                }

                RecordProcessingQueueMetrics(totalMs);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process frame #{FrameNumber} in async pipeline.", item.FrameNumber);
                _frameStateStore.SetLastError(ex);
            }
        }
    }

    private sealed class ProcessingQueueMetrics
    {
        private const double BackpressureThresholdMilliseconds = 1.0;

        private bool _enabled;
        private int _backpressureEvents;
        private double _lastWaitMilliseconds;
        private double _peakWaitMilliseconds;
        private double _totalWaitMilliseconds;
        private int _waitSamples;
        private double _lastProcessingMilliseconds;
        private double _peakProcessingMilliseconds;
        private double _totalProcessingMilliseconds;
        private int _processingSamples;

        public bool Enabled => _enabled;

        public void Reset(bool enabled)
        {
            _enabled = enabled;
            _backpressureEvents = 0;
            _lastWaitMilliseconds = 0;
            _peakWaitMilliseconds = 0;
            _totalWaitMilliseconds = 0;
            _waitSamples = 0;
            _lastProcessingMilliseconds = 0;
            _peakProcessingMilliseconds = 0;
            _totalProcessingMilliseconds = 0;
            _processingSamples = 0;
        }

        public void RecordEnqueue(double waitMilliseconds)
        {
            _lastWaitMilliseconds = waitMilliseconds;

            if (waitMilliseconds > _peakWaitMilliseconds)
            {
                _peakWaitMilliseconds = waitMilliseconds;
            }

            if (waitMilliseconds >= BackpressureThresholdMilliseconds)
            {
                _backpressureEvents++;
            }

            _totalWaitMilliseconds += waitMilliseconds;
            _waitSamples++;
        }

        public void RecordProcessing(double processingMilliseconds)
        {
            _lastProcessingMilliseconds = processingMilliseconds;

            if (processingMilliseconds > _peakProcessingMilliseconds)
            {
                _peakProcessingMilliseconds = processingMilliseconds;
            }

            _totalProcessingMilliseconds += processingMilliseconds;
            _processingSamples++;
        }

        public ProcessingQueueStatus CreateStatus(DateTimeOffset timestamp, int depth, int capacity)
        {
            var averageWait = _waitSamples > 0
                ? _totalWaitMilliseconds / _waitSamples
                : 0;

            var averageProcessing = _processingSamples > 0
                ? _totalProcessingMilliseconds / _processingSamples
                : 0;

            return new ProcessingQueueStatus(
                timestamp,
                Enabled: _enabled,
                Capacity: capacity,
                Depth: Math.Max(0, depth),
                BackpressureEvents: _backpressureEvents,
                LastEnqueueWaitMilliseconds: _lastWaitMilliseconds,
                PeakEnqueueWaitMilliseconds: _peakWaitMilliseconds,
                AverageEnqueueWaitMilliseconds: averageWait,
                LastProcessingMilliseconds: _lastProcessingMilliseconds,
                PeakProcessingMilliseconds: _peakProcessingMilliseconds,
                AverageProcessingMilliseconds: averageProcessing);
        }
    }

    private readonly record struct ProcessingWorkItem(
        int FrameNumber,
        CapturedImage CapturedFrame,
        CameraConfiguration Configuration,
        int ConfigurationVersion,
        bool UsingBackgroundStacker,
        double CaptureMilliseconds,
        Stopwatch FrameStopwatch,
        DateTimeOffset CapturedAtLocal);

    private readonly record struct ProcessingResult(
        bool UsingBackgroundStacker,
        bool Enqueued,
        double StackMilliseconds,
        double FilterMilliseconds,
        double EnqueueMilliseconds,
        DateTimeOffset CapturedAtLocal);
}
