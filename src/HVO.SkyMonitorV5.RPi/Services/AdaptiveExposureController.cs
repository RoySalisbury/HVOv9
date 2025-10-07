using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Services;

public sealed class AdaptiveExposureController : IExposureController
{
    private readonly IOptionsMonitor<CameraPipelineOptions> _optionsMonitor;

    public AdaptiveExposureController(IOptionsMonitor<CameraPipelineOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public ExposureSettings CreateNextExposure(CameraConfiguration configuration)
    {
        var options = _optionsMonitor.CurrentValue;
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.Local).AddHours(options.DayNightTransitionHourOffset);
        var isDay = nowLocal.Hour is >= 6 and < 18;

        var exposure = isDay ? options.DayExposureMilliseconds : options.NightExposureMilliseconds;
        var gain = isDay ? options.DayGain : options.NightGain;

        return new ExposureSettings(
            ExposureMilliseconds: exposure,
            Gain: gain,
            AutoExposure: false,
            AutoGain: false);
    }
}
