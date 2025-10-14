using System;
using System.Collections.Generic;
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
    SKImage? ImmutableImage = null);
