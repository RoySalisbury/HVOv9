using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Captures descriptive metadata that accompanies a frame export payload.
/// </summary>
public sealed record FrameExportMetadata(
    Guid FrameId,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset StageTimestampUtc,
    ExposureSettings Exposure,
    string RigName,
    string CameraName,
    string LensName,
    double LatitudeDeg,
    double LongitudeDeg,
    bool FlipHorizontal,
    double? HorizonPadding,
    bool ApplyRefraction,
    int? FramesStacked,
    int? IntegrationMilliseconds,
    IReadOnlyList<string>? AppliedFilters,
    double? QueueLatencyMilliseconds,
    double? ProcessingMilliseconds,
    double? FullPipelineMilliseconds,
    FrameExportImageDescriptor? RawImageDescriptor,
    string? PayloadContentType,
    string? PayloadExtension);
