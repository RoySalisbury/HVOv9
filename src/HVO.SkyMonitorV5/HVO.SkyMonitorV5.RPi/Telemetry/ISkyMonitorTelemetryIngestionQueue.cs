using System.Collections.Generic;
using System.Threading;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal interface ISkyMonitorTelemetryIngestionQueue
{
    bool TryWrite(TelemetryWorkItem workItem);

    IAsyncEnumerable<TelemetryWorkItem> ReadAllAsync(CancellationToken cancellationToken);

    int PendingCount { get; }
}
