using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents the operational status of the SkyMonitor capture pipeline.
/// </summary>
public sealed record AllSkyStatusResponse(
    bool IsRunning,
    DateTimeOffset? LastFrameTimestamp,
    ExposureSettings? LastExposure,
    CameraConfiguration? Configuration,
    ProcessedFrameSummary? ProcessedFrame,
    RawFrameSummary? RawFrame,
    BackgroundStackerStatus? BackgroundStacker,
    CameraDescriptor Camera,
    RigSpec? Rig,
    AllSkyStatusSummary Summary
);

public sealed record AllSkyStatusSummary(
    AllSkyCameraSummary Camera,
    AllSkyRigSummary? Rig,
    CameraConfiguration? Configuration,
    ProcessedFrameSummary? ProcessedFrame,
    RawFrameSummary? RawFrame,
    BackgroundStackerStatus? BackgroundStacker
);

public sealed record AllSkyCameraSummary(
    string Name,
    IReadOnlyList<string> Capabilities,
    int ExposureMilliseconds,
    int Gain,
    string Status
);

public sealed record AllSkyRigSummary(
    string Name,
    AllSkySensorSummary? Sensor,
    AllSkyLensSummary? Lens,
    string Status
);

public sealed record AllSkySensorSummary(
    int WidthPx,
    int HeightPx,
    double PixelSizeMicrons,
    string Status
);

public sealed record AllSkyLensSummary(
    string Name,
    string Kind,
    string Model,
    double FocalLengthMm,
    double FovXDeg,
    double? FovYDeg,
    string Status
);

public sealed record RawFrameSummary(
    DateTimeOffset Timestamp,
    int Width,
    int Height,
    int ExposureMilliseconds,
    int Gain
);
