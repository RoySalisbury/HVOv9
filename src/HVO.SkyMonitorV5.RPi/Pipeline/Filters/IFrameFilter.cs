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

    /// <summary>Applies the filter to the specified bitmap.</summary>
    ValueTask ApplyAsync(SKBitmap bitmap, FrameStackResult stackResult, CameraConfiguration configuration, CancellationToken cancellationToken);
}
