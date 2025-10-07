using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

public interface IFrameStacker
{
    FrameStackResult Accumulate(CameraFrame frame, CameraConfiguration configuration);

    void Reset();
}
