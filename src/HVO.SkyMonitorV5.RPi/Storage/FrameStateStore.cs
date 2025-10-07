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
    private CameraFrame? _latestRawFrame;
    private DateTimeOffset? _lastFrameTimestamp;
    private bool _isRunning;
    private Exception? _lastError;
    private CameraDescriptor? _cameraDescriptor;

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

    public CameraFrame? LatestRawFrame
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

    public void UpdateFrame(CameraFrame rawFrame, ProcessedFrame processedFrame)
    {
        lock (_sync)
        {
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

    public void UpdateCameraDescriptor(CameraDescriptor descriptor)
    {
        lock (_sync)
        {
            _cameraDescriptor = descriptor;
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

            return new AllSkyStatusResponse(
                IsRunning: _isRunning,
                LastFrameTimestamp: _lastFrameTimestamp,
                LastExposure: _latestRawFrame?.Exposure,
                Camera: descriptor,
                Configuration: _configuration,
                ProcessedFrame: CreateSummary(_latestProcessedFrame));
        }
    }

    private static ProcessedFrameSummary? CreateSummary(ProcessedFrame? frame)
    {
        if (frame is null)
        {
            return null;
        }

        return new ProcessedFrameSummary(frame.FramesCombined, frame.IntegrationMilliseconds);
    }
}
