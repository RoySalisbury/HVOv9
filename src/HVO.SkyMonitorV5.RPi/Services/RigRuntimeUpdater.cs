using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Catalog;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptionsDefaults = Microsoft.Extensions.Options.Options;

namespace HVO.SkyMonitorV5.RPi.Services;

public interface IRigRuntimeUpdater
{
    Task ReloadActiveRigAsync(bool forceRestart, CancellationToken cancellationToken);
}

public sealed class RigRuntimeUpdater : IRigRuntimeUpdater
{
    private readonly IRigAcquisitionAdapter _rigAdapter;
    private readonly IRigCatalog _rigCatalog;
    private readonly IOptionsMonitorCache<AllSkyCatalogOptions> _catalogCache;
    private readonly ILogger<RigRuntimeUpdater>? _logger;

    public RigRuntimeUpdater(
        IRigAcquisitionAdapter rigAdapter,
        IRigCatalog rigCatalog,
        IOptionsMonitorCache<AllSkyCatalogOptions> catalogCache,
        ILogger<RigRuntimeUpdater>? logger = null)
    {
        _rigAdapter = rigAdapter ?? throw new ArgumentNullException(nameof(rigAdapter));
        _rigCatalog = rigCatalog ?? throw new ArgumentNullException(nameof(rigCatalog));
        _catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
        _logger = logger;
    }

    public async Task ReloadActiveRigAsync(bool forceRestart, CancellationToken cancellationToken)
    {
        try
        {
            _catalogCache.TryRemove(OptionsDefaults.DefaultName);

            var resolved = _rigCatalog.ResolveActive();
            if (resolved.IsFailure)
            {
                if (resolved.Error is not null)
                {
                    _logger?.LogWarning(resolved.Error, "Rig reload skipped; active rig could not be resolved.");
                }
                else
                {
                    _logger?.LogWarning("Rig reload skipped; active rig could not be resolved.");
                }

                return;
            }

            var rig = resolved.Value;
            var reloadResult = await _rigAdapter
                .ReloadAsync(rig, cancellationToken, forceRestart)
                .ConfigureAwait(false);

            if (reloadResult.IsFailure)
            {
                _logger?.LogError(reloadResult.Error, "Rig acquisition adapter reload failed for {RigName}.", rig.Name);
                return;
            }

            if (reloadResult.Value)
            {
                _logger?.LogInformation("Rig acquisition adapter reloaded with rig {RigName}.", rig.Name);
            }
            else if (forceRestart)
            {
                _logger?.LogInformation(
                    "Rig acquisition adapter restart requested; rig {RigName} remained unchanged.",
                    rig.Name);
            }
            else
            {
                _logger?.LogDebug(
                    "Rig acquisition adapter already aligned with rig {RigName}; no reload required.",
                    rig.Name);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogDebug("Rig runtime reload cancelled by request context.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unhandled exception while reloading rig runtime state.");
        }
    }
}
