#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Filters;

/// <summary>
/// Extended filter contract that operates directly on pooled <see cref="FilterFrame"/> surfaces
/// rather than legacy <see cref="SKBitmap"/> copies.
/// </summary>
public interface IImageFrameFilter : IFrameFilter
{
    /// <summary>
    /// Applies the filter to the supplied filter frame.
    /// Implementations may render directly to the provided surface.
    /// </summary>
    ValueTask ApplyAsync(
        FilterFrame frame,
        FrameStackResult stackResult,
        CameraConfiguration configuration,
        FrameRenderContext? renderContext,
        CancellationToken cancellationToken);
}
