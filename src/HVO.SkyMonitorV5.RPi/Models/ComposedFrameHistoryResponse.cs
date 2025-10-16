using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Summary details for a previously composed frame, excluding image payloads.
/// </summary>
public sealed record ComposedFrameHistorySample(
    Guid FrameId,
    DateTimeOffset Timestamp,
    int Width,
    int Height,
    int FramesStacked,
    int IntegrationMilliseconds,
    IReadOnlyList<string> AppliedFilters,
    double SurfaceMilliseconds,
    IReadOnlyList<FilterExecution> FilterExecutions);

/// <summary>
/// Aggregated composed frame history response for diagnostics.
/// </summary>
public sealed record ComposedFrameHistoryResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ComposedFrameHistorySample> Frames);
