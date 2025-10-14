using System;
using HVO.SkyMonitorV5.RPi.Exports;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Represents the latest raw frame retained for downstream consumers.
/// Ownership of the <see cref="Image"/> belongs to the snapshot and will be disposed
/// when replaced.
/// </summary>
public sealed record RawFrameSnapshot(
    Guid FrameId,
    SKBitmap Image,
    DateTimeOffset Timestamp,
    ExposureSettings Exposure)
{
    public SKImage? ImmutableImage { get; init; }
    public FrameExportImageDescriptor? ImageDescriptor { get; init; }
}
