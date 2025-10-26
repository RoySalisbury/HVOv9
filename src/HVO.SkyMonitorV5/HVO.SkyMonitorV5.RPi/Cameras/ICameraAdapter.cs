using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Cameras;

/// <summary>
/// Defines a contract for camera adapters that can capture frames for the SkyMonitor pipeline.
/// </summary>
public interface ICameraAdapter : IAsyncDisposable
{
    RigSpec Rig { get; }

    Task<Result<bool>> InitializeAsync(CancellationToken cancellationToken);

    Task<Result<bool>> ShutdownAsync(CancellationToken cancellationToken);

    Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken);
}
