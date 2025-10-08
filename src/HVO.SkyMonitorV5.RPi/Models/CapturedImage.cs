using System;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents a freshly captured raw image plus metadata supplied by a camera adapter.
/// </summary>
public sealed record CapturedImage(
    SKBitmap Image,
    DateTimeOffset Timestamp,
    ExposureSettings Exposure,
    FrameContext? Context);
