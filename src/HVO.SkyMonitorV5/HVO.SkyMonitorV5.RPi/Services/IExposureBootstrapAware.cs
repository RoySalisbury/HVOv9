namespace HVO.SkyMonitorV5.RPi.Services;

/// <summary>
/// Provides a hook to reset exposure controller state when a capture session begins.
/// </summary>
public interface IExposureBootstrapAware
{
    /// <summary>
    /// Notifies the controller that a new capture session is starting so it can reset bootstrap tracking.
    /// </summary>
    void BeginCaptureSession();
}
