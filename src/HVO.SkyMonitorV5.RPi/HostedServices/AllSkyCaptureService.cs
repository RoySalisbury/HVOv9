using System;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using HVO.SkyMonitorV5.RPi.Services;
using HVO.SkyMonitorV5.RPi.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace HVO.SkyMonitorV5.RPi.HostedServices;

public sealed class AllSkyCaptureService : BackgroundService
{
    private readonly ILogger<AllSkyCaptureService> _logger;
    private readonly ICameraAdapter _cameraAdapter;
    private readonly IExposureController _exposureController;
    private readonly IFrameStacker _frameStacker;
    private readonly IFrameStackerConfigurationListener? _frameStackerConfigurationListener;
    private readonly IFrameFilterPipeline _frameFilterPipeline;
    private readonly IFrameStateStore _frameStateStore;
    private readonly IBackgroundFrameStacker _backgroundFrameStacker;
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;
    private readonly IObservatoryClock _clock;

    private const int MinimumFrameDelayMilliseconds = 250;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private int _frameNumber;

    public AllSkyCaptureService(
        ILogger<AllSkyCaptureService> logger,
        ICameraAdapter cameraAdapter,
        IExposureController exposureController,
        IFrameStacker frameStacker,
        IFrameFilterPipeline frameFilterPipeline,
        IFrameStateStore frameStateStore,
        IBackgroundFrameStacker backgroundFrameStacker,
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor,
        IObservatoryClock clock)
    {
        _logger = logger;
        _cameraAdapter = cameraAdapter;
        _exposureController = exposureController;
        _frameStacker = frameStacker;
    _frameStackerConfigurationListener = frameStacker as IFrameStackerConfigurationListener;
        _frameFilterPipeline = frameFilterPipeline;
        _frameStateStore = frameStateStore;
        _backgroundFrameStacker = backgroundFrameStacker;
        _optionsMonitor = optionsMonitor;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
    _logger.LogInformation("SkyMonitor capture service starting.");
    _frameStateStore.UpdateRig(_cameraAdapter.Rig);

        while (!stoppingToken.IsCancellationRequested)
        {
            var initResult = await _cameraAdapter.InitializeAsync(stoppingToken);
            if (initResult.IsFailure)
            {
                var initException = initResult.Error ?? new InvalidOperationException("Camera initialization failed with unknown error.");
                _frameStateStore.SetLastError(initException);
                _logger.LogError(initException, "Unable to initialize camera adapter. Retrying in {DelaySeconds}s", RetryDelay.TotalSeconds);
                await DelayWithCancellation(RetryDelay, stoppingToken);
                continue;
            }

            try
            {
                _frameStateStore.UpdateRunningState(true);
                await RunCaptureLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown requested
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in capture loop. Will attempt to reinitialize camera.");
                _frameStateStore.SetLastError(ex);
            }
            finally
            {
                _frameStateStore.UpdateRunningState(false);
                var shutdownResult = await _cameraAdapter.ShutdownAsync(CancellationToken.None);
                if (shutdownResult.IsFailure)
                {
                    _logger.LogWarning(shutdownResult.Error, "Camera adapter shutdown reported an error.");
                }
                _frameStateStore.SetLastError(null);
            }
        }

        _logger.LogInformation("SkyMonitor capture service stopping.");
    }

    private async Task RunCaptureLoopAsync(CancellationToken stoppingToken)
    {
        var configurationVersion = _frameStateStore.ConfigurationVersion;
        var configuration = _frameStateStore.Configuration;

        while (!stoppingToken.IsCancellationRequested)
        {
            var usingBackgroundStacker = _backgroundFrameStacker.IsEnabled;
            configurationVersion = CheckForConfigurationUpdates(configurationVersion, ref configuration, usingBackgroundStacker);

            var frameStopwatch = Stopwatch.StartNew();
            double captureMs = 0;
            double stackMs = 0;
            double filterMs = 0;
            double enqueueMs = 0;

            var exposure = _exposureController.CreateNextExposure(configuration);
            _logger.LogTrace("Prepared exposure {ExposureMs}ms / Gain {Gain}", exposure.ExposureMilliseconds, exposure.Gain);

            var captureStopwatch = Stopwatch.StartNew();
            var captureResult = await _cameraAdapter.CaptureAsync(exposure, stoppingToken);
            captureStopwatch.Stop();
            captureMs = captureStopwatch.Elapsed.TotalMilliseconds;
            if (captureResult.IsFailure)
            {
                await HandleCaptureFailureAsync(captureResult.Error, stoppingToken);
                continue;
            }

            var capturedFrame = captureResult.Value;
            var frameNumber = ++_frameNumber;
            var enqueued = false;
            var capturedAtLocal = _clock.ToLocal(capturedFrame.Timestamp);

            if (usingBackgroundStacker)
            {
                var workItem = new StackingWorkItem(frameNumber, capturedFrame, configuration, configurationVersion, DateTimeOffset.UtcNow);
                var enqueueStopwatch = Stopwatch.StartNew();
                enqueued = await _backgroundFrameStacker.EnqueueAsync(workItem, stoppingToken);
                enqueueStopwatch.Stop();
                enqueueMs = enqueueStopwatch.Elapsed.TotalMilliseconds;

                if (!enqueued)
                {
                    _logger.LogWarning(
                        "Background stacker rejected frame #{FrameNumber}; falling back to synchronous processing.",
                        frameNumber);
                }
            }

            if (!usingBackgroundStacker || !enqueued)
            {
                var (stack, filter) = await ProcessFrameSynchronouslyAsync(capturedFrame, configuration, stoppingToken);
                stackMs = stack;
                filterMs = filter;
            }

            frameStopwatch.Stop();
            var totalMs = frameStopwatch.Elapsed.TotalMilliseconds;

            var captureInterval = _optionsMonitor.CurrentValue.CaptureIntervalMilliseconds;
            var remainingMs = captureInterval - (int)Math.Round(totalMs);
            var delayMs = Math.Max(remainingMs, 0);
            if (delayMs < MinimumFrameDelayMilliseconds)
            {
                delayMs = MinimumFrameDelayMilliseconds;
            }

            if (usingBackgroundStacker && enqueued)
            {
                _logger.LogDebug(
                    "Captured frame #{FrameNumber} at {TimestampLocal} (capture {CaptureMs:F1}ms, enqueue {EnqueueMs:F1}ms, total {TotalMs:F1}ms). Next capture in {Delay}ms.",
                    frameNumber,
                    capturedAtLocal,
                    captureMs,
                    enqueueMs,
                    totalMs,
                    delayMs);
            }
            else
            {
                _logger.LogDebug(
                    "Captured frame #{FrameNumber} at {TimestampLocal} (capture {CaptureMs:F1}ms, stack {StackMs:F1}ms, filters {FilterMs:F1}ms, total {TotalMs:F1}ms). Next capture in {Delay}ms.",
                    frameNumber,
                    capturedAtLocal,
                    captureMs,
                    stackMs,
                    filterMs,
                    totalMs,
                    delayMs);
            }

            await DelayWithCancellation(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
        }
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
        _logger.LogError(error, "Camera capture failed. Retrying after short delay.");

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
}
