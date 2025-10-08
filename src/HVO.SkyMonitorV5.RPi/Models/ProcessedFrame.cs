namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents a processed frame ready for distribution to clients.
/// </summary>
public sealed record ProcessedFrame(
    DateTimeOffset Timestamp,
    ExposureSettings Exposure,
    byte[] ImageBytes,
    string ContentType,
    int FramesStacked,
    int IntegrationMilliseconds);
