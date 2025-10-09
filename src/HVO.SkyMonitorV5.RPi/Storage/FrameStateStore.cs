using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Options;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Storage;

public sealed class FrameStateStore : IFrameStateStore, IDisposable
{
    private readonly object _sync = new();
    private readonly ILogger<FrameStateStore>? _logger;
    private readonly IDisposable? _optionsReloadSubscription;

    private CameraConfiguration _configuration;
    private int _configurationVersion;
    private ProcessedFrame? _latestProcessedFrame;
    private RawFrameSnapshot? _latestRawFrame;
    private DateTimeOffset? _lastFrameTimestamp;
    private bool _isRunning;
    private Exception? _lastError;
    private CameraDescriptor? _cameraDescriptor;
    private RigSpec? _rigSpec;

    public FrameStateStore(IOptionsMonitor<CameraPipelineOptions> optionsMonitor, ILogger<FrameStateStore>? logger = null)
    {
        if (optionsMonitor is null)
        {
            throw new ArgumentNullException(nameof(optionsMonitor));
        }

        _logger = logger;
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
            _latestRawFrame = rawFrame;
            _latestProcessedFrame = processedFrame;
            _lastFrameTimestamp = processedFrame.Timestamp;
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

            var rig = _rigSpec;

            return new AllSkyStatusResponse(
                IsRunning: _isRunning,
                LastFrameTimestamp: _lastFrameTimestamp,
                LastExposure: _latestRawFrame?.Exposure,
                Camera: descriptor,
                Configuration: _configuration,
                ProcessedFrame: CreateSummary(_latestProcessedFrame),
                Rig: rig);
        }
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

    private static ProcessedFrameSummary? CreateSummary(ProcessedFrame? frame)
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
}
