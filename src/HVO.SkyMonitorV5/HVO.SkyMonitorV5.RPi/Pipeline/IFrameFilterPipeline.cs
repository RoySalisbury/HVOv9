using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

public interface IFrameFilterPipeline
{
    Task<ProcessedFrame> ProcessAsync(FrameStackResult stackResult,
                                      CameraConfiguration configuration,
                                      CancellationToken cancellationToken);
}
