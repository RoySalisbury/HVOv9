using System.Threading;
using System.Threading.Tasks;

namespace HVO.SkyMonitorV5.RPi.ImageHistory;

/// <summary>
/// Contract for enqueueing frames that should be persisted to the image history archive.
/// </summary>
public interface IImageFrameArchiveIngestionQueue
{
    /// <summary>
    /// Attempts to enqueue an archive ingestion request without blocking.
    /// </summary>
    bool TryEnqueue(ImageFrameArchiveIngestionRequest request);

    /// <summary>
    /// Enqueues an archive ingestion request, waiting for space in the internal channel if necessary.
    /// </summary>
    ValueTask<bool> EnqueueAsync(ImageFrameArchiveIngestionRequest request, CancellationToken cancellationToken = default);
}
