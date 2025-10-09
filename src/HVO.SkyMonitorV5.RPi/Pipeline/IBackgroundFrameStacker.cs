using System.Threading;
using System.Threading.Tasks;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

public interface IBackgroundFrameStacker
{
    bool IsEnabled { get; }

    ValueTask<bool> EnqueueAsync(StackingWorkItem workItem, CancellationToken cancellationToken);
}
