using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Options;
using System.Threading;

namespace HVO.SkyMonitorV5.RPi.Storage;

public sealed class FrameStateStore : IFrameStateStore
{
    private readonly object _sync = new();

    private CameraConfiguration _configuration;
    private int _configurationVersion;
    private ProcessedFrame? _latestProcessedFrame;
    private RawFrameSnapshot? _latestRawFrame;
    private DateTimeOffset? _lastFrameTimestamp;
    private bool _isRunning;
    private Exception? _lastError;
    private CameraDescriptor? _cameraDescriptor;
    private RigSpec? _rigSpec;

    public FrameStateStore(IOptions<CameraPipelineOptions> options)
    {
        _configuration = CameraConfiguration.FromOptions(options.Value);
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
        lock (_sync)
        {
            _configuration = configuration;
            _configurationVersion++;
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
