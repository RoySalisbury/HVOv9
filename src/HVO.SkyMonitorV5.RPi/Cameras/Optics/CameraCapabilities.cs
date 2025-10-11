#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics;

/// <summary>
/// Describes the hardware characteristics and operational features of a camera body.
/// </summary>
public sealed record CameraCapabilities
{
    public CameraColorMode ColorMode { get; init; } = CameraColorMode.Unknown;

    public CameraSensorTechnology SensorTechnology { get; init; } = CameraSensorTechnology.Unknown;

    public CameraBodyType BodyType { get; init; } = CameraBodyType.Unknown;

    public CameraCoolingType Cooling { get; init; } = CameraCoolingType.None;

    public bool SupportsGainControl { get; init; } = true;

    public bool SupportsExposureControl { get; init; } = true;

    public bool SupportsTemperatureTelemetry { get; init; }

    public bool SupportsSoftwareBinning { get; init; } = true;

    public IReadOnlyList<string> AdditionalTags { get; init; } = Array.Empty<string>();

    public static CameraCapabilities Empty { get; } = new();

    public CameraCapabilities()
    {
    }

    public CameraCapabilities(
        CameraColorMode colorMode,
        CameraSensorTechnology sensorTechnology,
        CameraBodyType bodyType,
        CameraCoolingType cooling,
        bool supportsGainControl,
        bool supportsExposureControl,
        bool supportsTemperatureTelemetry,
        bool supportsSoftwareBinning,
        IReadOnlyCollection<string>? additionalTags = null)
    {
        ColorMode = colorMode;
        SensorTechnology = sensorTechnology;
        BodyType = bodyType;
        Cooling = cooling;
        SupportsGainControl = supportsGainControl;
        SupportsExposureControl = supportsExposureControl;
        SupportsTemperatureTelemetry = supportsTemperatureTelemetry;
        SupportsSoftwareBinning = supportsSoftwareBinning;
        AdditionalTags = additionalTags is null
            ? Array.Empty<string>()
            : additionalTags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool IsCooled => Cooling != CameraCoolingType.None;

    public bool IsSynthetic => BodyType == CameraBodyType.Synthetic;
}

public enum CameraColorMode
{
    Unknown = 0,
    Monochrome,
    Color,
    Switchable
}

public enum CameraSensorTechnology
{
    Unknown = 0,
    Cmos,
    Ccd
}

public enum CameraBodyType
{
    Unknown = 0,
    DedicatedAstronomy,
    Dslr,
    Mirrorless,
    Industrial,
    Synthetic
}

public enum CameraCoolingType
{
    None = 0,
    Passive,
    Regulated
}
