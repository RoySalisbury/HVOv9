using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Storage;

public interface IFrameStateStore
{
    CameraConfiguration Configuration { get; }

    int ConfigurationVersion { get; }

    CameraDescriptor? CameraDescriptor { get; }

    RigSpec? Rig { get; }

    ProcessedFrame? LatestProcessedFrame { get; }

    RawFrameSnapshot? LatestRawFrame { get; }

    DateTimeOffset? LastFrameTimestamp { get; }

    bool IsRunning { get; }

    Exception? LastError { get; }

    void UpdateConfiguration(CameraConfiguration configuration);

    void UpdateFrame(RawFrameSnapshot rawFrame, ProcessedFrame processedFrame);

    void UpdateRunningState(bool isRunning);

    void UpdateRig(RigSpec rig);

    void SetLastError(Exception? exception);

    AllSkyStatusResponse GetStatus();
}
