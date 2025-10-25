using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HVO.SkyMonitorV5.RPi.Cameras;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

/// <summary>
/// Discovers <see cref="ICameraAdapter"/> implementations decorated with <see cref="CameraDriverAttribute"/>.
/// </summary>
public sealed class CameraDriverRegistry : ICameraDriverRegistry
{
    private readonly ILogger<CameraDriverRegistry>? _logger;
    private readonly IReadOnlyDictionary<string, CameraDriverDescriptor> _drivers;
    private readonly IReadOnlyCollection<CameraDriverDescriptor> _driverSnapshot;

    public CameraDriverRegistry(ILogger<CameraDriverRegistry>? logger = null)
    {
        _logger = logger;
        _drivers = DiscoverDrivers();
        _driverSnapshot = _drivers.Values.ToArray();
    }

    public IReadOnlyCollection<CameraDriverDescriptor> GetDrivers() => _driverSnapshot;

    public bool TryGetDriver(string id, out CameraDriverDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            descriptor = null!;
            return false;
        }

        return _drivers.TryGetValue(id, out descriptor!);
    }

    private IReadOnlyDictionary<string, CameraDriverDescriptor> DiscoverDrivers()
    {
        var descriptors = new Dictionary<string, CameraDriverDescriptor>(StringComparer.OrdinalIgnoreCase);
        var resolvedDrivers = new List<string>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            var candidateTypes = GetLoadableTypes(assembly);
            foreach (var type in candidateTypes)
            {
                if (type is null || !type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                if (!typeof(ICameraAdapter).IsAssignableFrom(type))
                {
                    continue;
                }

                var attribute = type.GetCustomAttribute<CameraDriverAttribute>(inherit: false);
                if (attribute is null)
                {
                    continue;
                }

                try
                {
                    CameraDriverAttribute.Validate(type, attribute);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Camera driver {DriverType} failed attribute validation and will be skipped.", type.FullName);
                    continue;
                }

                if (descriptors.ContainsKey(attribute.Id))
                {
                    var existing = descriptors[attribute.Id];
                    _logger?.LogError(
                        "Duplicate camera driver id '{DriverId}' detected between {ExistingType} and {NewType}. Skipping duplicate registration.",
                        attribute.Id,
                        existing.ImplementationType.FullName,
                        type.FullName);
                    continue;
                }

                ObjectFactory factory;
                try
                {
                    factory = ActivatorUtilities.CreateFactory(type, new[] { typeof(RigSpec) });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to create factory for camera driver {DriverType}.", type.FullName);
                    continue;
                }

                var descriptor = new CameraDriverDescriptor(
                    attribute.Id,
                    attribute.DisplayName,
                    attribute.Description,
                    attribute.Version,
                    type,
                    attribute.ConfigurationType,
                    factory);

                descriptors.Add(attribute.Id, descriptor);
                resolvedDrivers.Add(attribute.Id);
            }
        }

        if (resolvedDrivers.Count > 0)
        {
            var ordered = resolvedDrivers.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
            _logger?.LogInformation("Discovered {DriverCount} camera drivers: {DriverIds}.", descriptors.Count, string.Join(", ", ordered));
        }
        else
        {
            _logger?.LogWarning("Camera driver registry discovered no drivers.");
        }

        return descriptors;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
