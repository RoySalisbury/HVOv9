#nullable enable
using System;
using System.Text.Json.Serialization;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Models;

namespace HVO.SkyMonitorV5.RPi.Cameras.Optics;

/// <summary>
/// Camera body specification including sensor geometry, hardware capabilities, and descriptor metadata.
/// </summary>
public sealed record CameraSpec
{
    [JsonConstructor]
    public CameraSpec(
        string Name,
        SensorSpec Sensor,
        CameraCapabilities Capabilities,
        CameraDescriptor Descriptor,
        CameraDriverId DriverId = CameraDriverId.Unknown,
        bool IsSynthetic = false,
        string? SyntheticProfile = null,
        string? DriverSettingsJson = null)
    {
        this.Name = Name;
        this.Sensor = Sensor;
        this.Capabilities = Capabilities;
        this.Descriptor = Descriptor;
        this.DriverId = DriverId;
        this.IsSynthetic = IsSynthetic;
        this.SyntheticProfile = SyntheticProfile;
        this.DriverSettingsJson = DriverSettingsJson;
    }

    public CameraSpec(string name, SensorSpec sensor)
        : this(name, sensor, CameraCapabilities.Empty, CreateDefaultDescriptor(name))
    {
    }

    public CameraSpec(string name, SensorSpec sensor, CameraCapabilities capabilities)
        : this(name, sensor, capabilities, CreateDefaultDescriptor(name))
    {
    }

    public string Name { get; init; }

    public SensorSpec Sensor { get; init; }

    public CameraCapabilities Capabilities { get; init; }

    public CameraDescriptor Descriptor { get; init; }

    public CameraDriverId DriverId { get; init; }

    public bool IsSynthetic { get; init; }

    public string? SyntheticProfile { get; init; }

    public string? DriverSettingsJson { get; init; }

    public bool RequiresDriverRegistration => DriverId != CameraDriverId.Unknown || !string.IsNullOrWhiteSpace(DriverIdentifierOverride);

    public string DriverIdentifier => string.IsNullOrWhiteSpace(DriverIdentifierOverride)
        ? ResolveDriverIdentifier()
        : DriverIdentifierOverride;

    public string? DriverIdentifierOverride { get; init; }

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

    private string ResolveDriverIdentifier()
    {
        return DriverId switch
        {
            CameraDriverId.Synthetic => Capabilities.ColorMode switch
            {
                CameraColorMode.Color or CameraColorMode.Switchable => CameraDriverIdentifiers.SimulationMockColor,
                _ => CameraDriverIdentifiers.SimulationMockMono
            },
            CameraDriverId.Zwo => CameraDriverIdentifiers.ZwoAsi,
            _ => string.Empty
        };
    }
}
