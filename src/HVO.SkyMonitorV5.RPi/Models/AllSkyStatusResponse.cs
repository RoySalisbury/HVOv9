namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents the operational status of the SkyMonitor capture pipeline.
/// </summary>
public sealed record AllSkyStatusResponse(
    bool IsRunning,
    DateTimeOffset? LastFrameTimestamp,
    ExposureSettings? LastExposure,
    CameraDescriptor Camera,
    CameraConfiguration Configuration);
