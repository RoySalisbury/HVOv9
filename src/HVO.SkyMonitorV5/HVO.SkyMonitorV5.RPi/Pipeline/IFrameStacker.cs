using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Pipeline;

public interface IFrameStacker
{
    FrameStackResult Accumulate(CapturedImage frame, CameraConfiguration configuration);

    void Reset();
}

public interface IFrameStackerConfigurationListener
{
    void OnConfigurationChanged(CameraConfiguration previousConfiguration, CameraConfiguration currentConfiguration);
}
