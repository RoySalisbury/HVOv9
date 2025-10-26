using System;
using HVO.SkyMonitorV5.RPi.Skia;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents a freshly captured raw image plus metadata supplied by a camera adapter.
/// </summary>
public sealed record CapturedImage(
    Guid FrameId,
    SKBitmap Image,
    DateTimeOffset Timestamp,
    ExposureSettings Exposure,
    FrameContext? Context)
{
    public SKImage? ImmutableImage { get; init; }
    public SkiaPixelLease? PixelLease { get; init; }
}
