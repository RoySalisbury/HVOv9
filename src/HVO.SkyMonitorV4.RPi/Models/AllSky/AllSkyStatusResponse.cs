#nullable enable

namespace HVO.SkyMonitorV4.RPi.Models.AllSky;

public sealed record AllSkyStatusResponse(
    bool IsRecording,
    DateTimeOffset? LastImageTimestamp,
    bool HasRecentImage,
    string? LatestImageRelativePath,
    double MaxAttemptedFps,
    double ImageCircleRotationAngle,
    AllSkyExposureResponse Exposure);
