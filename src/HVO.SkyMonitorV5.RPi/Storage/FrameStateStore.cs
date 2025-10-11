using System;
using System.Collections.Generic;
using System.Linq;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;

namespace HVO.SkyMonitorV5.RPi.Storage;

public sealed class FrameStateStore : IFrameStateStore, IDisposable
{
    private readonly object _sync = new();
    private readonly ILogger<FrameStateStore>? _logger;
    private readonly IDisposable? _optionsReloadSubscription;
    private readonly IObservatoryClock _clock;

    private CameraConfiguration _configuration;
    private int _configurationVersion;
    private ProcessedFrame? _latestProcessedFrame;
    private RawFrameSnapshot? _latestRawFrame;
    private DateTimeOffset? _lastFrameTimestamp;
    private bool _isRunning;
    private Exception? _lastError;
    private CameraDescriptor? _cameraDescriptor;
    private RigSpec? _rigSpec;
    private BackgroundStackerStatus? _backgroundStackerStatus;
    private readonly Queue<BackgroundStackerHistorySample> _backgroundStackerHistory = new();
    private const int BackgroundStackerHistoryCapacity = 720;

    public FrameStateStore(IOptionsMonitor<CameraPipelineOptions> optionsMonitor, IObservatoryClock clock, ILogger<FrameStateStore>? logger = null)
    {
        if (optionsMonitor is null)
        {
            throw new ArgumentNullException(nameof(optionsMonitor));
        }

        _logger = logger;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _configuration = CameraConfiguration.FromOptions(optionsMonitor.CurrentValue);
    _optionsReloadSubscription = optionsMonitor.OnChange(OnPipelineOptionsChanged);
    }

    public CameraConfiguration Configuration
    {
        get
        {
            lock (_sync)
            {
                return _configuration;
            }
        }
    }

    public int ConfigurationVersion => Volatile.Read(ref _configurationVersion);

    public CameraDescriptor? CameraDescriptor
    {
        get
        {
            lock (_sync)
            {
                return _cameraDescriptor;
            }
        }
    }

    public RigSpec? Rig
    {
        get
        {
            lock (_sync)
            {
                return _rigSpec;
            }
        }
    }

    public ProcessedFrame? LatestProcessedFrame
    {
        get
        {
            lock (_sync)
            {
                return _latestProcessedFrame;
            }
        }
    }

    public RawFrameSnapshot? LatestRawFrame
    {
        get
        {
            lock (_sync)
            {
                return _latestRawFrame;
            }
        }
    }

    public DateTimeOffset? LastFrameTimestamp
    {
        get
        {
            lock (_sync)
            {
                return _lastFrameTimestamp;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _isRunning;
            }
        }
    }

    public Exception? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    public BackgroundStackerStatus? BackgroundStackerStatus
    {
        get
        {
            lock (_sync)
            {
                return _backgroundStackerStatus;
            }
        }
    }

    public void UpdateConfiguration(CameraConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (TryUpdateConfiguration(configuration, force: true, out var newVersion))
        {
            _logger?.LogInformation(
                "Camera configuration updated via API. Configuration version is now {ConfigurationVersion}.",
                newVersion);
        }
    }

    public void UpdateFrame(RawFrameSnapshot rawFrame, ProcessedFrame processedFrame)
    {
        lock (_sync)
        {
            if (_latestRawFrame is not null && !ReferenceEquals(_latestRawFrame, rawFrame))
            {
                _latestRawFrame.Image.Dispose();
            }
            var localizedRaw = rawFrame with { Timestamp = _clock.ToLocal(rawFrame.Timestamp) };
            var localizedProcessed = processedFrame with { Timestamp = _clock.ToLocal(processedFrame.Timestamp) };

            _latestRawFrame = localizedRaw;
            _latestProcessedFrame = localizedProcessed;
            _lastFrameTimestamp = localizedProcessed.Timestamp;
        }
    }

    public void UpdateRunningState(bool isRunning)
    {
        lock (_sync)
        {
            _isRunning = isRunning;
        }
    }

    public void UpdateRig(RigSpec rig)
    {
        lock (_sync)
        {
            _rigSpec = rig;
            _cameraDescriptor = rig.Descriptor ?? _cameraDescriptor;
        }
    }

    public void SetLastError(Exception? exception)
    {
        lock (_sync)
        {
            _lastError = exception;
        }
    }

    public void UpdateBackgroundStackerStatus(BackgroundStackerStatus status)
    {
        if (status is null)
        {
            throw new ArgumentNullException(nameof(status));
        }

        lock (_sync)
        {
            var localizedStatus = status with
            {
                LastEnqueuedAt = status.LastEnqueuedAt is { } enqueued ? _clock.ToLocal(enqueued) : null,
                LastCompletedAt = status.LastCompletedAt is { } completed ? _clock.ToLocal(completed) : null
            };

            _backgroundStackerStatus = localizedStatus;
            EnqueueBackgroundStackerSample(localizedStatus);
        }
    }

    public IReadOnlyList<BackgroundStackerHistorySample> GetBackgroundStackerHistory()
    {
        lock (_sync)
        {
            if (_backgroundStackerHistory.Count == 0)
            {
                return Array.Empty<BackgroundStackerHistorySample>();
            }

            return _backgroundStackerHistory.ToArray();
        }
    }

    public void Dispose()
    {
    _optionsReloadSubscription?.Dispose();
    }

    public AllSkyStatusResponse GetStatus()
    {
        lock (_sync)
        {
            var descriptor = _cameraDescriptor ?? new CameraDescriptor(
                Manufacturer: "Unknown",
                Model: "Unknown",
                DriverVersion: "Unknown",
                AdapterName: "Unknown",
                Capabilities: Array.Empty<string>());

            var processedSummary = CreateProcessedSummary(_latestProcessedFrame);
            var rawSummary = CreateRawSummary(_latestRawFrame);
            var exposure = _latestRawFrame?.Exposure;
            var rigSpec = _rigSpec;
            var rig = CreateRigSummary(rigSpec);
            var cameraSummary = CreateCameraSummary(descriptor, rigSpec, exposure, _isRunning, _lastError);

            var summary = new AllSkyStatusSummary(
                Camera: cameraSummary,
                Rig: rig,
                Configuration: _configuration,
                ProcessedFrame: processedSummary,
                RawFrame: rawSummary,
                BackgroundStacker: _backgroundStackerStatus);

            return new AllSkyStatusResponse(
                IsRunning: _isRunning,
                LastFrameTimestamp: _lastFrameTimestamp,
                LastExposure: exposure,
                Configuration: _configuration,
                ProcessedFrame: processedSummary,
                RawFrame: rawSummary,
                BackgroundStacker: _backgroundStackerStatus,
                Camera: descriptor,
                Rig: rigSpec,
                Summary: summary);
        }
    }

    private void EnqueueBackgroundStackerSample(BackgroundStackerStatus status)
    {
        var sample = new BackgroundStackerHistorySample(
            Timestamp: _clock.LocalNow,
            QueueFillPercentage: status.QueueFillPercentage,
            QueueDepth: status.QueueDepth,
            QueueCapacity: status.QueueCapacity,
            QueueLatencyMilliseconds: status.LastQueueLatencyMilliseconds,
            StackDurationMilliseconds: status.LastStackMilliseconds,
            FilterDurationMilliseconds: status.LastFilterMilliseconds,
            QueuePressureLevel: status.QueuePressureLevel,
            SecondsSinceLastCompleted: status.SecondsSinceLastCompleted,
            QueueMemoryMegabytes: status.QueueMemoryMegabytes);

        if (_backgroundStackerHistory.Count >= BackgroundStackerHistoryCapacity)
        {
            _backgroundStackerHistory.Dequeue();
        }

        _backgroundStackerHistory.Enqueue(sample);
    }

    private void OnPipelineOptionsChanged(CameraPipelineOptions options)
    {
        if (options is null)
        {
            return;
        }

        var updatedConfiguration = CameraConfiguration.FromOptions(options);
        if (TryUpdateConfiguration(updatedConfiguration, force: false, out var newVersion))
        {
            _logger?.LogInformation(
                "Camera pipeline options reloaded from configuration; version advanced to {ConfigurationVersion}.",
                newVersion);
        }
    }

    private bool TryUpdateConfiguration(CameraConfiguration configuration, bool force, out int newVersion)
    {
        lock (_sync)
        {
            if (!force && _configuration.Equals(configuration))
            {
                newVersion = _configurationVersion;
                return false;
            }

            _configuration = configuration;
            newVersion = ++_configurationVersion;
            return true;
        }
    }

    private static ProcessedFrameSummary? CreateProcessedSummary(ProcessedFrame? frame)
    {
        if (frame is null)
        {
            return null;
        }

        return new ProcessedFrameSummary(
            frame.FramesStacked,
            frame.IntegrationMilliseconds,
            frame.AppliedFilters,
            frame.ProcessingMilliseconds);
    }

    private static RawFrameSummary? CreateRawSummary(RawFrameSnapshot? frame)
    {
        if (frame is null)
        {
            return null;
        }

        var width = frame.Image?.Width ?? 0;
        var height = frame.Image?.Height ?? 0;
        return new RawFrameSummary(
            Timestamp: frame.Timestamp,
            Width: width,
            Height: height,
            ExposureMilliseconds: frame.Exposure.ExposureMilliseconds,
            Gain: frame.Exposure.Gain);
    }

    private static AllSkyCameraSummary CreateCameraSummary(
        CameraDescriptor descriptor,
        RigSpec? rig,
        ExposureSettings? exposure,
        bool isRunning,
        Exception? lastError)
    {
        var name = string.IsNullOrWhiteSpace(descriptor.Model)
            ? descriptor.Manufacturer
            : FormattableString.Invariant($"{descriptor.Manufacturer} {descriptor.Model}").Trim();

        var status = lastError is not null
            ? "Error"
            : isRunning
                ? "Capturing"
                : "Idle";

        var pipelineCapabilities = descriptor.Capabilities as IReadOnlyList<string>
            ?? descriptor.Capabilities?.ToArray()
            ?? Array.Empty<string>();

        var hardwareCapabilities = rig?.Capabilities.ToDisplayTags() ?? Array.Empty<string>();

        return new AllSkyCameraSummary(
            Name: string.IsNullOrWhiteSpace(name) ? "Unknown" : name,
            Capabilities: pipelineCapabilities,
            HardwareCapabilities: hardwareCapabilities,
            ExposureMilliseconds: exposure?.ExposureMilliseconds ?? 0,
            Gain: exposure?.Gain ?? 0,
            Status: status);
    }

    private static AllSkyRigSummary? CreateRigSummary(RigSpec? rig)
    {
        if (rig is null)
        {
            return null;
        }

        var sensor = new AllSkySensorSummary(
            WidthPx: rig.Sensor.WidthPx,
            HeightPx: rig.Sensor.HeightPx,
            PixelSizeMicrons: rig.Sensor.PixelSizeMicrons,
            Status: "Configured");

        var lens = new AllSkyLensSummary(
            Name: string.IsNullOrWhiteSpace(rig.Lens.Name) ? rig.Lens.Kind.ToString() : rig.Lens.Name,
            Kind: rig.Lens.Kind.ToString(),
            Model: rig.Lens.Model.ToString(),
            FocalLengthMm: rig.Lens.FocalLengthMm,
            FovXDeg: rig.Lens.FovXDeg,
            FovYDeg: rig.Lens.FovYDeg,
            Status: "Configured");

        var status = "Configured";

        return new AllSkyRigSummary(
            Name: rig.Name,
            Sensor: sensor,
            Lens: lens,
            Status: status);
    }
}
