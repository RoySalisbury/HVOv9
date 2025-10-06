#nullable enable

using System.ComponentModel.DataAnnotations;
using HVO.ZWOOptical.ASISDK;

namespace HVO.SkyMonitorV4.RPi.HostedServices.AllSkyCamera;

public sealed class AllSkyCameraServiceOptions
{
    [Range(1, int.MaxValue)]
    public uint RestartOnFailureWaitTimeSeconds { get; set; } = 15;

    [Required]
    public string CacheImageSaveRoot { get; set; } = "/dev/shm/skymonitor";

    [Range(1, int.MaxValue)]
    public int AutoExposureMaxDurationMs { get; set; } = 20_000;

    [Range(0, int.MaxValue)]
    public int AutoExposureMaxGain { get; set; } = 200;

    [Range(0, int.MaxValue)]
    public int ExposureBrightness { get; set; } = 50;

    public bool AutoExposureBrightness { get; set; } = true;

    [Range(0, int.MaxValue)]
    public int AutoExposureTargetBrightness { get; set; } = 100;

    [Range(0, int.MaxValue)]
    public int ExposureGain { get; set; } = 0;

    public bool AutoExposureGain { get; set; } = true;

    [Range(0, int.MaxValue)]
    public int ExposureDurationMs { get; set; } = 0;

    public bool AutoExposureDuration { get; set; } = true;

    public double ImageCircleRotationAngle { get; set; } = 0.0d;

    [Range(0, int.MaxValue)]
    public int ImageCircleDiameter { get; set; } = 1088;

    [Range(0, int.MaxValue)]
    public int ImageCircleOffsetX { get; set; } = 368;

    [Range(0, int.MaxValue)]
    public int ImageCircleOffsetY { get; set; } = 8;

    [Range(1, int.MaxValue)]
    public int ImageHeight { get; set; } = 960;

    [Range(1, int.MaxValue)]
    public int ImageWidth { get; set; } = 1704;

    [Range(1, 144)]
    public double ImageTextFontSize { get; set; } = 17.5d;

    [Range(0.1, 30.0)]
    public double MaxAttemptedFps { get; set; } = 2.15d;

    [Range(1, 100)]
    public long JpegImageQuality { get; set; } = 90;

    [Range(1, 4)]
    public int ImageBinMode { get; set; } = 1;

    public ASICamera2.ASI_IMG_TYPE ImageType { get; set; } = ASICamera2.ASI_IMG_TYPE.ASI_IMG_RAW16;

    [Range(0, 15)]
    public int CameraIndex { get; set; } = 0;

    [Range(0, int.MaxValue)]
    public int CameraRoiWidth { get; set; } = 0;

    [Range(0, int.MaxValue)]
    public int CameraRoiHeight { get; set; } = 0;

    public bool UseImageCircleMask { get; set; } = true;

    public bool CleanCacheOnStart { get; set; } = true;

    [Required]
    public string LatestImageFileName { get; set; } = "latestImage.jpg";
}
