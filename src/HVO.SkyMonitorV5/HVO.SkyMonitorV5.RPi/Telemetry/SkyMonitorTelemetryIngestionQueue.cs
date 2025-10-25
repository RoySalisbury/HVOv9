using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace HVO.SkyMonitorV5.RPi.Telemetry;

internal sealed class SkyMonitorTelemetryIngestionQueue : ISkyMonitorTelemetryIngestionQueue
{
    private readonly Channel<TelemetryWorkItem> _channel;
    private int _pendingCount;

    public SkyMonitorTelemetryIngestionQueue()
    {
        _channel = Channel.CreateUnbounded<TelemetryWorkItem>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public int PendingCount => Volatile.Read(ref _pendingCount);

    public bool TryWrite(TelemetryWorkItem workItem)
    {
        if (_channel.Writer.TryWrite(workItem))
        {
            Interlocked.Increment(ref _pendingCount);
            return true;
        }

        return false;
    }

    public async IAsyncEnumerable<TelemetryWorkItem> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            bool canRead;

            try
            {
                canRead = await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            if (!canRead)
            {
                yield break;
            }

            while (_channel.Reader.TryRead(out var workItem))
            {
                Interlocked.Decrement(ref _pendingCount);
                yield return workItem;
            }
        }
    }
}
