using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Contract implemented by frame export sinks that persist frame payloads to external systems.
/// </summary>
public interface IFrameExportSink
{
    /// <summary>
    /// Gets the sink display name used for logging and diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Determines whether the sink can handle the specified stage.
    /// </summary>
    /// <param name="stage">The stage associated with the payload.</param>
    /// <returns><c>true</c> if the sink accepts the stage; otherwise, <c>false</c>.</returns>
    bool SupportsStage(FrameExportStage stage);

    /// <summary>
    /// Persists the supplied payload.
    /// </summary>
    /// <param name="envelope">The export envelope to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    ValueTask<Result<bool>> ExportAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken);
}
