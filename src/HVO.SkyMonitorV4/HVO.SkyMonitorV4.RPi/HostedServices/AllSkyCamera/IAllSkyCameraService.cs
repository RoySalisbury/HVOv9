#nullable enable

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyCamera;

public interface IAllSkyCameraService
{
    bool IsRecording { get; }

    DateTimeOffset LastImageTakenTimestamp { get; }

    string? LastImageRelativePath { get; }

    string ImageCacheRoot { get; }

    bool AutoExposureGain { get; set; }

    bool AutoExposureDuration { get; set; }

    bool AutoExposureBrightness { get; set; }

    int ExposureGain { get; set; }

    int ExposureDurationMilliseconds { get; set; }

    int AutoExposureMaxDurationMilliseconds { get; set; }

    int AutoExposureMaxGain { get; set; }

    int AutoExposureBrightnessTarget { get; set; }

    int ExposureBrightness { get; set; }

    double MaxAttemptedFps { get; set; }

    double ImageCircleRotationAngle { get; set; }

    void ClearLastImage();

    Task RunAsync(CancellationToken cancellationToken);
}
