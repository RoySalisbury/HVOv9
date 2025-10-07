namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents a raw frame captured from the camera adapter.
/// </summary>
public sealed record CameraFrame(
    DateTimeOffset Timestamp,
    ExposureSettings Exposure,
    byte[] ImageBytes,
    string ContentType,
    FramePixelBuffer? RawPixelBuffer = null);
