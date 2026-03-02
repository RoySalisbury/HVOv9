#nullable enable
using System;
using HVO.Core.Results;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

/// <summary>
/// Default implementation that instantiates camera adapters based on <see cref="CameraDriverId"/>.
/// </summary>
public sealed class CameraDriverFactory : ICameraDriverFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICameraDriverRegistry _registry;
    private readonly ILogger<CameraDriverFactory>? _logger;

    public CameraDriverFactory(
        IServiceProvider serviceProvider,
        ICameraDriverRegistry registry,
        ILogger<CameraDriverFactory>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;
    }

    public Result<ICameraAdapter> Create(RigSpec rig)
    {
        if (rig is null)
        {
            return Result<ICameraAdapter>.Failure(new ArgumentNullException(nameof(rig)));
        }

        try
        {
            var driverIdentifier = rig.Camera.DriverIdentifier;
            if (string.IsNullOrWhiteSpace(driverIdentifier))
            {
                return Result<ICameraAdapter>.Failure(new InvalidOperationException(
                    $"Rig '{rig.Name}' specifies unsupported camera driver id '{rig.Camera.DriverId}'."));
            }

            if (!_registry.TryGetDriver(driverIdentifier, out var descriptor))
            {
                return Result<ICameraAdapter>.Failure(new InvalidOperationException(
                    $"Camera driver '{driverIdentifier}' is not registered for rig '{rig.Name}'."));
            }

            var adapter = descriptor.Create(_serviceProvider, rig);
            _logger?.LogDebug(
                "Created camera driver {DriverId} ({DriverType}) for rig {RigName}.",
                descriptor.Id,
                descriptor.ImplementationType.Name,
                rig.Name);

            return Result<ICameraAdapter>.Success(adapter);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create camera driver for rig {RigName}.", rig.Name);
            return Result<ICameraAdapter>.Failure(ex);
        }
    }
}
