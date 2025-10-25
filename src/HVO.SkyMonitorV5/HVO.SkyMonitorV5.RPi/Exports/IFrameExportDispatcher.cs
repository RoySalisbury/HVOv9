using System.Threading;
using System.Threading.Tasks;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Provides a staging point between the capture/processing pipeline and frame export sinks.
/// </summary>
public interface IFrameExportDispatcher
{
    /// <summary>
    /// Attempts to enqueue an export envelope without waiting.
    /// </summary>
    /// <param name="envelope">The envelope to enqueue.</param>
    /// <returns><c>true</c> if the envelope was accepted; otherwise, <c>false</c>.</returns>
    bool TryEnqueue(FrameExportEnvelope envelope);

    /// <summary>
    /// Enqueues an export envelope, optionally waiting until capacity is available.
    /// </summary>
    /// <param name="envelope">The envelope to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the envelope was accepted; otherwise, <c>false</c>.</returns>
    ValueTask<bool> EnqueueAsync(FrameExportEnvelope envelope, CancellationToken cancellationToken = default);
}
