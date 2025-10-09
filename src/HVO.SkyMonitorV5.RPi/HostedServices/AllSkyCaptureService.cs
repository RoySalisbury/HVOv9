using System;
using HVO.SkyMonitorV5.RPi.Cameras;
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
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;

    private const int MinimumFrameDelayMilliseconds = 250;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    public AllSkyCaptureService(
        ILogger<AllSkyCaptureService> logger,
        ICameraAdapter cameraAdapter,
        IExposureController exposureController,
        IFrameStacker frameStacker,
        IFrameFilterPipeline frameFilterPipeline,
        IFrameStateStore frameStateStore,
        IOptionsMonitor<CameraPipelineOptions> optionsMonitor)
    {
        _logger = logger;
        _cameraAdapter = cameraAdapter;
        _exposureController = exposureController;
        _frameStacker = frameStacker;
    _frameStackerConfigurationListener = frameStacker as IFrameStackerConfigurationListener;
        _frameFilterPipeline = frameFilterPipeline;
        _frameStateStore = frameStateStore;
        _optionsMonitor = optionsMonitor;
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
            configurationVersion = CheckForConfigurationUpdates(configurationVersion, ref configuration);

            var frameStopwatch = Stopwatch.StartNew();
            double captureMs = 0;
            double stackMs = 0;
            double filterMs = 0;

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
            var stackStopwatch = Stopwatch.StartNew();
            var stackResult = _frameStacker.Accumulate(capturedFrame, configuration);
            stackStopwatch.Stop();
            stackMs = stackStopwatch.Elapsed.TotalMilliseconds;
            var frameContext = stackResult.Context;
            var frameStored = false;

            try
            {
                var filterStopwatch = Stopwatch.StartNew();
                var processedFrame = await _frameFilterPipeline.ProcessAsync(stackResult, configuration, stoppingToken);
                filterStopwatch.Stop();
                filterMs = filterStopwatch.Elapsed.TotalMilliseconds;

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

                frameStopwatch.Stop();
                var totalMs = frameStopwatch.Elapsed.TotalMilliseconds;

                var captureInterval = _optionsMonitor.CurrentValue.CaptureIntervalMilliseconds;
                var remainingMs = captureInterval - (int)Math.Round(totalMs);
                var delayMs = Math.Max(remainingMs, 0);
                if (delayMs < MinimumFrameDelayMilliseconds)
                {
                    delayMs = MinimumFrameDelayMilliseconds;
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Captured frame at {Timestamp} (capture {CaptureMs:F1}ms, stack {StackMs:F1}ms, filters {FilterMs:F1}ms, total {TotalMs:F1}ms). Next capture in {Delay}ms.",
                        processedFrame.Timestamp,
                        captureMs,
                        stackMs,
                        filterMs,
                        totalMs,
            delayMs);
                }

        await DelayWithCancellation(TimeSpan.FromMilliseconds(delayMs), stoppingToken);
            }
            finally
            {
                if (!frameStored)
                {
                    stackResult.OriginalImage.Dispose();
                }

                frameContext?.Dispose();
            }
        }
    }

    private int CheckForConfigurationUpdates(int currentVersion, ref CameraConfiguration configuration)
    {
        var latestVersion = _frameStateStore.ConfigurationVersion;
        if (latestVersion != currentVersion)
        {
            var previousConfiguration = configuration;
            configuration = _frameStateStore.Configuration;

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
