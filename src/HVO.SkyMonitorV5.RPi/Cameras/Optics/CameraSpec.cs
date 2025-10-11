#nullable enable
using System;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics;

/// <summary>
/// Camera body specification including sensor geometry, hardware capabilities, and descriptor metadata.
/// </summary>
public sealed record CameraSpec(
    string Name,
    SensorSpec Sensor,
    CameraCapabilities Capabilities,
    CameraDescriptor Descriptor)
{
    public CameraSpec(string name, SensorSpec sensor)
        : this(name, sensor, CameraCapabilities.Empty, CreateDefaultDescriptor(name))
    {
    }

    public CameraSpec(string name, SensorSpec sensor, CameraCapabilities capabilities)
        : this(name, sensor, capabilities, CreateDefaultDescriptor(name))
    {
    }

    private static CameraDescriptor CreateDefaultDescriptor(string name)
    {
        var label = string.IsNullOrWhiteSpace(name) ? "Unknown" : name.Trim();
        return new CameraDescriptor(
            Manufacturer: "Unknown",
            Model: label,
            DriverVersion: string.Empty,
            AdapterName: label,
            Capabilities: Array.Empty<string>());
    }
}
