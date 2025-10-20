using System;

namespace HVO.SkyMonitorV5.RPi.Models.Cameras;

public sealed class CameraDriverDescriptorResponse
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string? ConfigurationType { get; init; }

    public bool SupportsConfiguration { get; init; }

    public string? AssemblyQualifiedName { get; init; }
}
