#nullable enable
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Cameras.Acquisition;

/// <summary>
/// Coordinates capture, exposure analysis, and pipeline processing for a single active <see cref="RigSpec"/>.
/// </summary>
public interface IRigAcquisitionAdapter : IAsyncDisposable
{
    /// <summary>
    /// Gets the currently active rig specification driving acquisition.
    /// </summary>
    RigSpec ActiveRig { get; }

    /// <summary>
    /// Indicates whether the adapter is actively capturing frames.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the current lifecycle state of the adapter.
    /// </summary>
    RigAdapterLifecycleState CurrentState { get; }

    /// <summary>
    /// Starts acquisition for the active rig.
    /// </summary>
    Task<Result<bool>> StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Pauses capture without releasing pipeline resources.
    /// </summary>
    Task<Result<bool>> PauseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resumes capture after a pause.
    /// </summary>
    Task<Result<bool>> ResumeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops capture and gracefully tears down active driver resources.
    /// </summary>
    Task<Result<bool>> StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reloads the adapter with a new rig specification, restarting capture if previously running.
    /// When <paramref name="forceReload"/> is <c>true</c>, the underlying driver is reinitialized even if the
    /// incoming rig specification matches the current active rig.
    /// </summary>
    Task<Result<bool>> ReloadAsync(RigSpec rig, CancellationToken cancellationToken, bool forceReload = false);

    /// <summary>
    /// Captures an image using the active rig for the supplied exposure settings.
    /// </summary>
    Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken);
}

/// <summary>
/// Represents the lifecycle state of the rig acquisition adapter.
/// </summary>
public enum RigAdapterLifecycleState
{
    Stopped = 0,
    Running = 1,
    Paused = 2
}
