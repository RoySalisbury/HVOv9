using System;
using System.Collections.Generic;
using HVO.SkyMonitorV5.RPi.Pipeline.Composition;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents a processed frame ready for distribution to clients.
/// </summary>
public sealed record ProcessedFrame(
    Guid FrameId,
    DateTimeOffset Timestamp,
    ExposureSettings Exposure,
    byte[] ImageBytes,
    string ContentType,
    int FramesStacked,
    int IntegrationMilliseconds,
    IReadOnlyList<string> AppliedFilters,
    int ProcessingMilliseconds,
    SKImage? ImmutableImage = null)
{
    /// <summary>
    /// Detailed execution timings for each filter applied during composition.
    /// </summary>
    public IReadOnlyList<FilterExecution> FilterExecutions { get; init; } = Array.Empty<FilterExecution>();

    /// <summary>
    /// Time spent preparing the composition surface prior to filter execution, in milliseconds.
    /// </summary>
    public double SurfaceMilliseconds { get; init; }
}
