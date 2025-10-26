using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Exports;

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
    CapturePacingStatus? CapturePacing,
    ProcessingQueueStatus? ProcessingQueue,
    CameraDescriptor Camera,
    RigSpec? Rig,
    AllSkyStatusSummary Summary,
    ExposureProfileSummary? ExposureProfiles,
    ExposureAnalysisSummary? ExposureAnalysis,
    ExposureOverrideSummary? ExposureOverrides,
    RemoteDispatchStatus? RemoteDispatch
);

public sealed record AllSkyStatusSummary(
    AllSkyCameraSummary Camera,
    AllSkyRigSummary? Rig,
    CameraConfiguration? Configuration,
    ProcessedFrameSummary? ProcessedFrame,
    RawFrameSummary? RawFrame,
    BackgroundStackerStatus? BackgroundStacker,
    CapturePacingStatus? CapturePacing,
    ProcessingQueueStatus? ProcessingQueue,
    ExposureProfileSummary? ExposureProfiles,
    ExposureAnalysisSummary? ExposureAnalysis,
    ExposureOverrideSummary? ExposureOverrides,
    RemoteDispatchStatus? RemoteDispatch
);

public sealed record AllSkyCameraSummary(
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> HardwareCapabilities,
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
    int Gain,
    FrameExportImageDescriptor? ImageDescriptor
);

public sealed record ExposureAnalysisSummary(
    DateTimeOffset? Timestamp,
    ExposureLightingCondition LightingCondition,
    double AverageLuminance,
    double MinimumLuminance,
    double MaximumLuminance,
    int SampleCount,
    int? SuggestedExposureMilliseconds,
    int? SuggestedGain,
    string? Notes
);

public sealed record ExposureOverrideSummary(
    ExposureOverrideSnapshot? Day,
    ExposureOverrideSnapshot? Night
);

public sealed record ExposureOverrideSnapshot(
    ExposureOverrideBucket Bucket,
    DateTimeOffset? LastUpdated,
    DateTimeOffset? ExpiresAt,
    int BaselineExposureMilliseconds,
    int BaselineGain,
    int TargetExposureMilliseconds,
    int TargetGain,
    int AppliedExposureMilliseconds,
    int AppliedGain
);
