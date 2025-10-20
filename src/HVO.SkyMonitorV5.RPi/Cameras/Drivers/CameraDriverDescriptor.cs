using System;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using Microsoft.Extensions.DependencyInjection;

namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

/// <summary>
/// Represents a discovered camera driver along with metadata and creation factory.
/// </summary>
public sealed class CameraDriverDescriptor
{
    private readonly ObjectFactory _factory;

    internal CameraDriverDescriptor(
        string id,
        string displayName,
        string description,
        string version,
        Type implementationType,
        Type? configurationType,
        ObjectFactory factory)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Driver identifier must be provided.", nameof(id));
        }

        Id = id;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        Description = description ?? string.Empty;
        Version = version ?? string.Empty;
        ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
        ConfigurationType = configurationType;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string Version { get; }

    public Type ImplementationType { get; }

    public Type? ConfigurationType { get; }

    public ICameraAdapter Create(IServiceProvider serviceProvider, RigSpec rig)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(rig);

        var instance = _factory(serviceProvider, new object[] { rig });
        if (instance is not ICameraAdapter adapter)
        {
            throw new InvalidOperationException(
                $"Factory for driver '{Id}' returned incompatible type '{instance?.GetType().FullName ?? "null"}'.");
        }

        return adapter;
    }
}
