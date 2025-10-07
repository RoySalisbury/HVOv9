using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface IExposureController
{
    ExposureSettings CreateNextExposure(CameraConfiguration configuration);
}
