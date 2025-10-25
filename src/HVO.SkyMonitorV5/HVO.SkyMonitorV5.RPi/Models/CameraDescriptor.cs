namespace HVO.SkyMonitorV5.RPi.Models;

/// <summary>
/// Provides metadata about a camera adapter implementation.
/// </summary>
public sealed record CameraDescriptor(
    string Manufacturer,
    string Model,
    string DriverVersion,
    string AdapterName,
    IReadOnlyCollection<string> Capabilities);
