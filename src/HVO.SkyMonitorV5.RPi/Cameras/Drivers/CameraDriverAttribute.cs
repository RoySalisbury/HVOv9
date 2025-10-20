using System;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;

namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CameraDriverAttribute : Attribute
{
    private string _displayName = string.Empty;
    private string _description = string.Empty;
    private string _version = string.Empty;

    public CameraDriverAttribute(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Camera driver identifier must be provided.", nameof(id));
        }

        Id = id.Trim();
    }

    public string Id { get; }

    public string DisplayName
    {
        get => string.IsNullOrWhiteSpace(_displayName) ? Id : _displayName;
        set => _displayName = value?.Trim() ?? string.Empty;
    }

    public string Description
    {
        get => _description;
        set => _description = value?.Trim() ?? string.Empty;
    }

    public string Version
    {
        get => _version;
        set => _version = value?.Trim() ?? string.Empty;
    }

    public Type? ConfigurationType { get; set; }

    public static void Validate(Type targetType, CameraDriverAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(attribute);

        if (!typeof(ICameraAdapter).IsAssignableFrom(targetType))
        {
            throw new InvalidOperationException($"Camera driver attribute can only be applied to types implementing {nameof(ICameraAdapter)}.");
        }

        if (string.IsNullOrWhiteSpace(attribute.Id))
        {
            throw new InvalidOperationException($"Camera driver on type '{targetType.FullName}' must provide a non-empty id.");
        }

        if (attribute.ConfigurationType is not null && !attribute.ConfigurationType.IsClass)
        {
            throw new InvalidOperationException($"Camera driver '{attribute.Id}' declares configuration type '{attribute.ConfigurationType.FullName}', which is not a class.");
        }
    }
}
