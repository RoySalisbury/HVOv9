#nullable enable

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

public sealed class CircularApertureMaskFilter : IImageFrameFilter
{
	private readonly IOptionsMonitor<CircularApertureMaskOptions> _optionsMonitor;

	public CircularApertureMaskFilter(IOptionsMonitor<CircularApertureMaskOptions> optionsMonitor)
	{
		_optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
	}

	public string Name => FrameFilterNames.CircularApertureMask;

	public bool ShouldApply(CameraConfiguration configuration) => configuration.EnableCircularApertureMask;

	public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
		=> ApplyAsync(bitmap, stackResult, configuration, renderContext: null, cancellationToken);

	public ValueTask ApplyAsync(
		SKBitmap bitmap,
		FrameStackResult stackResult,
		CameraConfiguration configuration,
		FrameRenderContext? renderContext,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		using var canvas = new SKCanvas(bitmap);
		DrawMask(canvas, bitmap.Width, bitmap.Height, renderContext, cancellationToken);
		return ValueTask.CompletedTask;
	}

	public ValueTask ApplyAsync(
		FilterFrame frame,
		FrameStackResult stackResult,
		CameraConfiguration configuration,
		FrameRenderContext? renderContext,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var canvas = frame.Surface.Canvas;
		var projector = renderContext?.Projector;

		var width = projector?.WidthPx
			?? stackResult.StackedImmutableImage?.Width
			?? stackResult.StackedImage?.Width
			?? (int)MathF.Round(canvas.DeviceClipBounds.Width);
		var height = projector?.HeightPx
			?? stackResult.StackedImmutableImage?.Height
			?? stackResult.StackedImage?.Height
			?? (int)MathF.Round(canvas.DeviceClipBounds.Height);

		if (width <= 0 || height <= 0)
		{
			var bounds = canvas.DeviceClipBounds;
			width = (int)MathF.Round(bounds.Width);
			height = (int)MathF.Round(bounds.Height);
		}

		DrawMask(canvas, width, height, renderContext, cancellationToken);
		return ValueTask.CompletedTask;
	}

	private void DrawMask(
		SKCanvas canvas,
		int canvasWidth,
		int canvasHeight,
		FrameRenderContext? renderContext,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (canvasWidth <= 0 || canvasHeight <= 0)
		{
			return;
		}

		var options = _optionsMonitor.CurrentValue;

		var projector = renderContext?.Projector;
		var referenceWidth = projector?.WidthPx ?? canvasWidth;
		var referenceHeight = projector?.HeightPx ?? canvasHeight;
		var horizonPadding = (float)(renderContext?.HorizonPadding ?? 0.95);
		horizonPadding = Math.Clamp(horizonPadding, 0.1f, 1.2f);

		var baseRadius = Math.Min(referenceWidth, referenceHeight) * horizonPadding * 0.5f;
		var radius = Math.Max(8f, baseRadius + options.RadiusOffsetPixels);

		var center = projector is not null
			? new SKPoint((float)projector.Cx, (float)projector.Cy)
			: new SKPoint(canvasWidth / 2f, canvasHeight / 2f);

		center.Offset(options.OffsetXPixels, options.OffsetYPixels);

		var circleRect = new SKRect(
			center.X - radius,
			center.Y - radius,
			center.X + radius,
			center.Y + radius);

		using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
		path.AddRect(new SKRect(0, 0, canvasWidth, canvasHeight));
		path.AddOval(circleRect);

		var maskBaseColor = ResolveColor(options.MaskColor, new SKColor(0, 0, 0));
		var overlayColor = maskBaseColor.WithAlpha((byte)Math.Clamp(options.MaskOpacity, 0, 255));

		using var overlayPaint = new SKPaint { IsAntialias = true, Color = overlayColor };
		canvas.DrawPath(path, overlayPaint);
		canvas.Flush();
	}

	private static SKColor ResolveColor(string? color, SKColor fallback)
	{
		if (string.IsNullOrWhiteSpace(color))
		{
			return fallback;
		}

		var span = color.AsSpan().Trim();

		if (span.StartsWith("#", StringComparison.Ordinal))
		{
			span = span[1..];
		}

		if (span.Length is 6 or 8 && uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
		{
			if (span.Length == 6)
			{
				return new SKColor(
					(byte)((value & 0xFF0000) >> 16),
					(byte)((value & 0x00FF00) >> 8),
					(byte)(value & 0x0000FF));
			}

			var r = (byte)((value & 0x00FF0000) >> 16);
			var g = (byte)((value & 0x0000FF00) >> 8);
			var b = (byte)(value & 0x000000FF);
			var a = (byte)((value & 0xFF000000) >> 24);
			return new SKColor(r, g, b, a);
		}

		return fallback;
	}
}
