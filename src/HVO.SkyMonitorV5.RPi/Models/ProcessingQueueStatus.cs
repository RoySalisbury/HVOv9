using System;

namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Describes the state of the asynchronous processing queue when capture and processing are decoupled.
/// </summary>
public sealed record ProcessingQueueStatus(
    DateTimeOffset Timestamp,
    bool Enabled,
    int Capacity,
    int Depth,
    int BackpressureEvents,
    double LastEnqueueWaitMilliseconds,
    double PeakEnqueueWaitMilliseconds,
    double AverageEnqueueWaitMilliseconds,
    double LastProcessingMilliseconds,
    double PeakProcessingMilliseconds,
    double AverageProcessingMilliseconds)
{
    public static ProcessingQueueStatus Disabled(DateTimeOffset timestamp) => new(
        timestamp,
        Enabled: false,
        Capacity: 0,
        Depth: 0,
        BackpressureEvents: 0,
        LastEnqueueWaitMilliseconds: 0,
        PeakEnqueueWaitMilliseconds: 0,
        AverageEnqueueWaitMilliseconds: 0,
        LastProcessingMilliseconds: 0,
        PeakProcessingMilliseconds: 0,
        AverageProcessingMilliseconds: 0);
}
