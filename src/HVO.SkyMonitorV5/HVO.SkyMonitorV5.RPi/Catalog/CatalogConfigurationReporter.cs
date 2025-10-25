#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Catalog;

/// <summary>
/// Logs catalog configuration status and highlights legacy inline rig usage during startup.
/// </summary>
public sealed class CatalogConfigurationReporter : IHostedService
{
    private readonly AllSkyCatalogRegistry _catalogRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CatalogConfigurationReporter> _logger;

    public CatalogConfigurationReporter(
        AllSkyCatalogRegistry catalogRegistry,
        IConfiguration configuration,
        ILogger<CatalogConfigurationReporter> logger)
    {
        _catalogRegistry = catalogRegistry ?? throw new ArgumentNullException(nameof(catalogRegistry));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var stats = _catalogRegistry.GetStatistics();
        _logger.LogInformation(
            "AllSky catalog loaded: {CameraCount} cameras, {LensCount} lenses, {RigCount} rigs. Active rig: {ActiveRig}",
            stats.CameraCount,
            stats.LensCount,
            stats.RigCount,
            stats.ActiveRigName ?? "<none>");

        var cameras = _configuration
            .GetSection(CameraAdapterOptions.SectionName)
            .Get<IReadOnlyList<CameraAdapterOptions>>() ?? Array.Empty<CameraAdapterOptions>();

        var inlineRigCameras = cameras
            .Where(static c => c is not null)
            .Where(c => string.IsNullOrWhiteSpace(c.RigCatalog) && c.Rig is not null)
            .Select(c => c.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (inlineRigCameras.Length > 0)
        {
            _logger.LogWarning(
                "Legacy inline rig definitions detected for cameras: {Cameras}. These will continue working via the temporary shim but should be migrated to catalog entries.",
                inlineRigCameras);
        }
        else if (stats.RigCount == 0)
        {
            _logger.LogWarning("No rig catalog entries are defined. Camera adapters will rely on inline rig configuration until migration is complete.");
        }

        if (stats.ActiveRigName is null && stats.RigCount > 0)
        {
            _logger.LogInformation("No active rig configured; defaulting to the first catalog entry for runtime operations.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
