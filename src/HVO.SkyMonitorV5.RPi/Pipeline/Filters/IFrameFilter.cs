#nullable enable

using HVO.SkyMonitorV5.RPi.Models;
using SkiaSharp;
using System.Threading;
using System.Threading.Tasks;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

/// <summary>
/// Represents an individual filter step in the frame processing pipeline.
/// </summary>
public interface IFrameFilter
{
    /// <summary>Gets the unique name used to reference this filter in configuration.</summary>
    string Name { get; }

    /// <summary>Determines whether the filter should be applied for the provided configuration.</summary>
    bool ShouldApply(CameraConfiguration configuration);

    /// <summary>Applies the filter to the specified bitmap (legacy signature without context).</summary>
    ValueTask ApplyAsync(SKBitmap bitmap,
                         FrameStackResult stackResult,
                         CameraConfiguration configuration,
                         CancellationToken cancellationToken);

    /// <summary>
    /// Applies the filter to the specified bitmap, with a per-frame render context.
    /// Default implementation forwards to the legacy signature so existing filters
    /// keep working without changes.
    /// </summary>
    ValueTask ApplyAsync(SKBitmap bitmap,
                         FrameStackResult stackResult,
                         CameraConfiguration configuration,
                         FrameRenderContext? renderContext,
                         CancellationToken cancellationToken)
        => ApplyAsync(bitmap, stackResult, configuration, cancellationToken);
}
