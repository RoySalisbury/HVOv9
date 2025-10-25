using System.Threading;
using System.Threading.Tasks;

namespace HVO.SkyMonitorV5.RPi.Exports;

/// <summary>
/// Coordinates persistence and scheduling of frame export retry requests.
/// </summary>
public interface IFrameExportRetryQueue
{
    /// <summary>
    /// Schedules the specified export envelope for a future retry attempt.
    /// </summary>
    /// <param name="request">The retry request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ScheduleRetryAsync(FrameExportRetryRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Represents a failed export attempt that should be retried in the future.
/// </summary>
/// <param name="Envelope">The export envelope to retry.</param>
/// <param name="SinkName">The sink that failed.</param>
/// <param name="AttemptCount">The number of attempts that have already been made.</param>
/// <param name="ErrorMessage">Optional error information from the failed attempt.</param>
public sealed record FrameExportRetryRequest(
    FrameExportEnvelope Envelope,
    string SinkName,
    int AttemptCount,
    string? ErrorMessage);
