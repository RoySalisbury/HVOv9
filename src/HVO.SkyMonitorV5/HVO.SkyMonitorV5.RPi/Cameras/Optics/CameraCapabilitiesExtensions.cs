#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics;

public static class CameraCapabilitiesExtensions
{
    public static IReadOnlyList<string> ToDisplayTags(this CameraCapabilities? capabilities)
    {
        if (capabilities is null)
        {
            return Array.Empty<string>();
        }

    var tags = new List<string>();

        AppendColorMode(tags, capabilities.ColorMode);
        AppendSensorTechnology(tags, capabilities.SensorTechnology);
        AppendBodyType(tags, capabilities.BodyType);
        AppendCooling(tags, capabilities.Cooling);

        if (capabilities.SupportsGainControl)
        {
            AddTag(tags, "Gain Control");
        }

        if (capabilities.SupportsExposureControl)
        {
            AddTag(tags, "Exposure Control");
        }

        if (capabilities.SupportsSoftwareBinning)
        {
            AddTag(tags, "Software Binning");
        }

        if (capabilities.SupportsTemperatureTelemetry)
        {
            AddTag(tags, "Temperature Telemetry");
        }

        if (capabilities.IsSynthetic)
        {
            AddTag(tags, "Synthetic");
        }

        if (capabilities.AdditionalTags.Count > 0)
        {
            foreach (var tag in capabilities.AdditionalTags)
            {
                AddTag(tags, tag);
            }
        }

        return tags.Count == 0
            ? Array.Empty<string>()
            : tags;
    }

    private static void AppendColorMode(List<string> tags, CameraColorMode colorMode)
    {
        switch (colorMode)
        {
            case CameraColorMode.Monochrome:
                AddTag(tags, "Monochrome");
                break;
            case CameraColorMode.Color:
                AddTag(tags, "Color");
                break;
            case CameraColorMode.Switchable:
                AddTag(tags, "Mono/Color Switchable");
                break;
        }
    }

    private static void AppendSensorTechnology(List<string> tags, CameraSensorTechnology technology)
    {
        switch (technology)
        {
            case CameraSensorTechnology.Cmos:
                AddTag(tags, "CMOS");
                break;
            case CameraSensorTechnology.Ccd:
                AddTag(tags, "CCD");
                break;
        }
    }

    private static void AppendBodyType(List<string> tags, CameraBodyType bodyType)
    {
        switch (bodyType)
        {
            case CameraBodyType.Dslr:
                AddTag(tags, "DSLR");
                break;
            case CameraBodyType.Mirrorless:
                AddTag(tags, "Mirrorless");
                break;
            case CameraBodyType.DedicatedAstronomy:
                AddTag(tags, "Astro Camera");
                break;
            case CameraBodyType.Industrial:
                AddTag(tags, "Industrial");
                break;
            case CameraBodyType.Synthetic:
                AddTag(tags, "Synthetic");
                break;
        }
    }

    private static void AppendCooling(List<string> tags, CameraCoolingType cooling)
    {
        switch (cooling)
        {
            case CameraCoolingType.Passive:
                AddTag(tags, "Passive Cooling");
                break;
            case CameraCoolingType.Regulated:
                AddTag(tags, "Regulated Cooling");
                break;
        }
    }

    private static void AddTag(List<string> tags, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!tags.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(value);
        }
    }
}
