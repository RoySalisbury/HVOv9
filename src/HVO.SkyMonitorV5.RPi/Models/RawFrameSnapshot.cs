using System;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents the latest raw frame retained for downstream consumers.
/// Ownership of the <see cref="Image"/> belongs to the snapshot and will be disposed
/// when replaced.
/// </summary>
public sealed record RawFrameSnapshot(
    SKBitmap Image,
    DateTimeOffset Timestamp,
    ExposureSettings Exposure);
