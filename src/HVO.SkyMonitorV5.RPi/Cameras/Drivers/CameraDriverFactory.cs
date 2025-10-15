#nullable enable
using System;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Cameras.Zwo;
using HVO.SkyMonitorV5.RPi.Infrastructure;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Pipeline.Preprocessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Cameras.Drivers;

/// <summary>
/// Default implementation that instantiates camera adapters based on <see cref="CameraDriverId"/>.
/// </summary>
public sealed class CameraDriverFactory : ICameraDriverFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CameraDriverFactory>? _logger;

    public CameraDriverFactory(IServiceProvider serviceProvider, ILogger<CameraDriverFactory>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
            return rig.Camera.DriverId switch
            {
                CameraDriverId.Synthetic => Result<ICameraAdapter>.Success(CreateSyntheticAdapter(rig)),
                CameraDriverId.Zwo => Result<ICameraAdapter>.Success(CreateZwoAdapter(rig)),
                _ => Result<ICameraAdapter>.Failure(new InvalidOperationException(
                    $"Unsupported camera driver id '{rig.Camera.DriverId}' for rig '{rig.Name}'."))
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create camera driver for rig {RigName}.", rig.Name);
            return Result<ICameraAdapter>.Failure(ex);
        }
    }

    private ICameraAdapter CreateSyntheticAdapter(RigSpec rig)
    {
        var clock = _serviceProvider.GetRequiredService<IObservatoryClock>();
        var locationOptions = _serviceProvider.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>();
        var starCatalogOptions = _serviceProvider.GetRequiredService<IOptionsMonitor<StarCatalogOptions>>();
        var cardinalOptions = _serviceProvider.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var preprocessor = _serviceProvider.GetService<IFramePreprocessingOrchestrator>();

        var colorMode = rig.Camera.Capabilities.ColorMode;
        if (colorMode is CameraColorMode.Color or CameraColorMode.Switchable)
        {
            return new MockColorCameraAdapter(
                locationOptions,
                starCatalogOptions,
                cardinalOptions,
                scopeFactory,
                rig,
                clock,
                _serviceProvider.GetService<ILoggerFactory>(),
                    _serviceProvider.GetService<ILogger<MockColorCameraAdapter>>(),
                    noiseProfile: null,
                    preprocessingOrchestrator: preprocessor);
        }

        return new MockCameraAdapter(
            locationOptions,
            starCatalogOptions,
            cardinalOptions,
            scopeFactory,
            rig,
            clock,
                _serviceProvider.GetService<ILogger<MockCameraAdapter>>(),
                preprocessingOrchestrator: preprocessor);
    }

    private ICameraAdapter CreateZwoAdapter(RigSpec rig)
    {
        var clock = _serviceProvider.GetRequiredService<IObservatoryClock>();
        var locationOptions = _serviceProvider.GetRequiredService<IOptionsMonitor<ObservatoryLocationOptions>>();
        var cardinalOptions = _serviceProvider.GetRequiredService<IOptionsMonitor<CardinalDirectionsOptions>>();
        var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
        return new ZwoCameraAdapter(
            rig,
            clock,
            locationOptions,
            cardinalOptions,
            loggerFactory,
                _serviceProvider.GetService<ILogger<ZwoCameraAdapter>>(),
                _serviceProvider.GetService<IFramePreprocessingOrchestrator>());
    }
}
