#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;
using SkiaSharp;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

public sealed class CircularMaskFilter : IFrameFilter
{
    public string Name => FrameFilterNames.CircularMask;

    public bool ShouldApply(CameraConfiguration configuration) => configuration.EnableMaskOverlay;

    public ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var canvas = new SKCanvas(bitmap);
        var diameter = Math.Min(bitmap.Width, bitmap.Height) * 0.95f;
        var offsetX = (bitmap.Width - diameter) / 2f;
        var offsetY = (bitmap.Height - diameter) / 2f;

        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRect(new SKRect(0, 0, bitmap.Width, bitmap.Height));
        path.AddOval(new SKRect(offsetX, offsetY, offsetX + diameter, offsetY + diameter));

        using var overlayPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 220) };
        canvas.DrawPath(path, overlayPaint);

        return ValueTask.CompletedTask;
    }
}
