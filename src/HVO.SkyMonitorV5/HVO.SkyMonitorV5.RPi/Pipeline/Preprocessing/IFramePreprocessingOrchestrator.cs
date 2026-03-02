#nullable enable

using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;
using HVO.SkyMonitorV5.RPi.Cameras;

namespace HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;

/// <summary>
/// Coordinates preprocessing passes that operate on captured frames prior to stacking.
/// </summary>
public interface IFramePreprocessingOrchestrator
{
    /// <summary>
    /// Executes preprocessing against the provided adapter frame, returning an updated frame on success.
    /// </summary>
    /// <remarks>
    /// Implementations should avoid mutating the incoming frame directly. Instead, construct a new frame via
    /// expression-based copies (<c>frame with { ... }</c>) so ownership of disposable resources remains explicit.
    /// </remarks>
    Task<Result<CameraAdapterBase.AdapterFrame>> ProcessAsync(CameraAdapterBase.AdapterFrame frame, CancellationToken cancellationToken);
}
