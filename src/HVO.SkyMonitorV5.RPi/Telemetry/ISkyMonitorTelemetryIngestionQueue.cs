using System.Threading;
using System.Threading.Channels;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal interface ISkyMonitorTelemetryIngestionQueue
{
    bool TryWrite(TelemetryWorkItem workItem);

    ChannelReader<TelemetryWorkItem> Reader { get; }
}
