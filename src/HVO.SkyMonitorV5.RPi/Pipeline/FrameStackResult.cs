using HVO.SkyMonitorV5.RPi.Models;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// Represents the outcome of stacking operations, including metadata for downstream processing.
/// </summary>
public sealed record FrameStackResult(
	SKBitmap StackedImage,
	SKBitmap OriginalImage,
	DateTimeOffset Timestamp,
	ExposureSettings Exposure,
	FrameContext? Context,
	int FramesStacked,
	int IntegrationMilliseconds);
