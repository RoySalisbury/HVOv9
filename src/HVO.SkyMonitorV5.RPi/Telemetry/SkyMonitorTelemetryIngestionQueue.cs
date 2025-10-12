using System.Threading.Channels;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class SkyMonitorTelemetryIngestionQueue : ISkyMonitorTelemetryIngestionQueue
{
    private readonly Channel<TelemetryWorkItem> _channel;

    public SkyMonitorTelemetryIngestionQueue()
    {
        _channel = Channel.CreateUnbounded<TelemetryWorkItem>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<TelemetryWorkItem> Reader => _channel.Reader;

    public bool TryWrite(TelemetryWorkItem workItem) => _channel.Writer.TryWrite(workItem);
}
