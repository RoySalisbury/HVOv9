#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Catalog;
using HVO.SkyMonitorV5.RPi.Cameras.Drivers;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.SkyMonitorV5.RPi.Cameras.Acquisition;

/// <summary>
/// Baseline implementation of <see cref="IRigAcquisitionAdapter"/> that manages lifecycle transitions.
/// Pipeline execution will be layered on in subsequent Phase 3 work.
/// </summary>
public sealed class RigAcquisitionAdapter : IRigAcquisitionAdapter
{
    private readonly IRigCatalog _rigCatalog;
    private readonly ICameraDriverFactory _cameraDriverFactory;
    private readonly ILogger<RigAcquisitionAdapter>? _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly IDisposable? _optionsChangeRegistration;

    private volatile AdapterState _state = AdapterState.Stopped;
    private RigSpec _activeRig;
    private ICameraAdapter? _driver;
    private bool _driverInitialized;
    private Task<Result<bool>>? _initializationTask;
    private bool _disposed;

    public RigAcquisitionAdapter(
        IRigCatalog rigCatalog,
        ICameraDriverFactory cameraDriverFactory,
        IOptionsMonitor<AllSkyCatalogOptions> optionsMonitor,
        ILogger<RigAcquisitionAdapter>? logger = null)
    {
        _rigCatalog = rigCatalog ?? throw new ArgumentNullException(nameof(rigCatalog));
        _cameraDriverFactory = cameraDriverFactory ?? throw new ArgumentNullException(nameof(cameraDriverFactory));
        if (optionsMonitor is null)
        {
            throw new ArgumentNullException(nameof(optionsMonitor));
        }

        _activeRig = ResolveActiveRig();
        _optionsChangeRegistration = optionsMonitor.OnChange((_, _) => _ = ReloadFromCatalogAsync());
        _logger = logger;
    }

    public RigSpec ActiveRig => _activeRig;

    public bool IsRunning => _state == AdapterState.Running;

    public async Task<Result<bool>> StartAsync(CancellationToken cancellationToken)
    {
        var initResult = await EnsureDriverInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (initResult.IsFailure)
        {
            return initResult;
        }

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return Result<bool>.Failure(new ObjectDisposedException(nameof(RigAcquisitionAdapter)));
            }

            if (_state == AdapterState.Running)
            {
                _logger?.LogDebug("StartAsync called while adapter already running for {RigName}.", _activeRig.Name);
                return Result<bool>.Success(false);
            }

            _state = AdapterState.Running;
            _logger?.LogInformation("Rig acquisition adapter started for {RigName}.", _activeRig.Name);
            return Result<bool>.Success(true);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<Result<bool>> PauseAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return Result<bool>.Failure(new ObjectDisposedException(nameof(RigAcquisitionAdapter)));
            }

            if (_state != AdapterState.Running)
            {
                _logger?.LogDebug("PauseAsync called while adapter not running for {RigName}.", _activeRig.Name);
                return Result<bool>.Success(false);
            }

            _state = AdapterState.Paused;
            _logger?.LogInformation("Rig acquisition adapter paused for {RigName}.", _activeRig.Name);
            return Result<bool>.Success(true);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<Result<bool>> ResumeAsync(CancellationToken cancellationToken)
    {
        var initResult = await EnsureDriverInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (initResult.IsFailure)
        {
            return initResult;
        }

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return Result<bool>.Failure(new ObjectDisposedException(nameof(RigAcquisitionAdapter)));
            }

            if (_state != AdapterState.Paused)
            {
                _logger?.LogDebug("ResumeAsync called while adapter not paused for {RigName}.", _activeRig.Name);
                return Result<bool>.Success(false);
            }

            _state = AdapterState.Running;
            _logger?.LogInformation("Rig acquisition adapter resumed for {RigName}.", _activeRig.Name);
            return Result<bool>.Success(true);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<Result<bool>> StopAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var alreadyStopped = false;
        try
        {
            if (_disposed)
            {
                return Result<bool>.Failure(new ObjectDisposedException(nameof(RigAcquisitionAdapter)));
            }

            if (_state == AdapterState.Stopped)
            {
                alreadyStopped = true;
            }
            else
            {
                _state = AdapterState.Stopped;
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (!alreadyStopped)
        {
            await ShutdownDriverAsync().ConfigureAwait(false);
            _logger?.LogInformation("Rig acquisition adapter stopped for {RigName}.", _activeRig.Name);
            return Result<bool>.Success(true);
        }

        _logger?.LogDebug("StopAsync called while adapter already stopped for {RigName}.", _activeRig.Name);
        return Result<bool>.Success(false);
    }

    public async Task<Result<bool>> ReloadAsync(RigSpec rig, CancellationToken cancellationToken, bool forceReload = false)
    {
        if (rig is null)
        {
            return Result<bool>.Failure(new ArgumentNullException(nameof(rig)));
        }

        ICameraAdapter? driverToReset = null;
        bool wasRunning;

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return Result<bool>.Failure(new ObjectDisposedException(nameof(RigAcquisitionAdapter)));
            }

            if (!forceReload && rig == _activeRig)
            {
                _logger?.LogDebug("ReloadAsync skipped; active rig already {RigName}.", rig.Name);
                return Result<bool>.Success(false);
            }

            wasRunning = _state == AdapterState.Running;
            driverToReset = _driver;
            _driverInitialized = false;
            _driver = null;
            _initializationTask = null;
            _activeRig = rig;
        }
        finally
        {
            _stateLock.Release();
        }

        if (driverToReset is not null)
        {
            await ResetDriverAsync(driverToReset, logErrors: true).ConfigureAwait(false);
        }

        if (wasRunning)
        {
            var initResult = await EnsureDriverInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (initResult.IsFailure)
            {
                await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    _state = AdapterState.Stopped;
                }
                finally
                {
                    _stateLock.Release();
                }

                _logger?.LogError(initResult.Error, "Failed to reinitialize driver after rig reload to {RigName}.", rig.Name);
                return initResult;
            }

            await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_state == AdapterState.Stopped)
                {
                    _state = AdapterState.Running;
                }
            }
            finally
            {
                _stateLock.Release();
            }

            _logger?.LogInformation("Rig acquisition adapter reloaded and running with rig {RigName}.", rig.Name);
        }
        else
        {
            _logger?.LogInformation("Rig acquisition adapter reloaded to rig {RigName}.", rig.Name);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<CapturedImage>> CaptureAsync(ExposureSettings exposure, CancellationToken cancellationToken)
    {
        if (exposure is null)
        {
            throw new ArgumentNullException(nameof(exposure));
        }

        ICameraAdapter? driver;

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return Result<CapturedImage>.Failure(new ObjectDisposedException(nameof(RigAcquisitionAdapter)));
            }

            if (_state != AdapterState.Running)
            {
                return Result<CapturedImage>.Failure(new InvalidOperationException("Rig acquisition adapter is not running."));
            }

            driver = _driver;
        }
        finally
        {
            _stateLock.Release();
        }

        if (driver is null || !_driverInitialized)
        {
            var initResult = await EnsureDriverInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (initResult.IsFailure)
            {
                return Result<CapturedImage>.Failure(initResult.Error ?? new InvalidOperationException("Unable to initialize camera driver."));
            }

            await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                driver = _driver;
            }
            finally
            {
                _stateLock.Release();
            }

            if (driver is null)
            {
                return Result<CapturedImage>.Failure(new InvalidOperationException("Camera driver unavailable after initialization."));
            }
        }

        return await driver.CaptureAsync(exposure, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await ShutdownDriverAsync().ConfigureAwait(false);

        await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state = AdapterState.Stopped;
            _logger?.LogDebug("Rig acquisition adapter disposed for {RigName}.", _activeRig.Name);
        }
        finally
        {
            _stateLock.Release();
            _optionsChangeRegistration?.Dispose();
            _stateLock.Dispose();
        }
    }

    private async Task<Result<bool>> EnsureDriverInitializedAsync(CancellationToken cancellationToken)
    {
        Task<Result<bool>> initTask;

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return Result<bool>.Failure(new ObjectDisposedException(nameof(RigAcquisitionAdapter)));
            }

            if (_driverInitialized && _driver is not null)
            {
                return Result<bool>.Success(true);
            }

            if (_initializationTask is not null)
            {
                initTask = _initializationTask;
            }
            else
            {
                if (_driver is null)
                {
                    var driverResult = _cameraDriverFactory.Create(_activeRig);
                    if (driverResult.IsFailure)
                    {
                        return Result<bool>.Failure(driverResult.Error ?? new InvalidOperationException("Failed to create camera driver."));
                    }

                    _driver = driverResult.Value;
                }

                if (_driver is null)
                {
                    return Result<bool>.Failure(new InvalidOperationException("Camera driver was not created."));
                }

                initTask = InitializeDriverInternalAsync(_driver, cancellationToken);
                _initializationTask = initTask;
            }
        }
        finally
        {
            _stateLock.Release();
        }

    var result = await initTask.ConfigureAwait(false);

        await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_initializationTask, initTask))
            {
                _initializationTask = null;
                if (!result.IsFailure)
                {
                    if (_disposed)
                    {
                        return Result<bool>.Failure(new ObjectDisposedException(nameof(RigAcquisitionAdapter)));
                    }

                    _driverInitialized = true;
                    _logger?.LogDebug("Camera driver {DriverId} initialized for rig {RigName}.", _activeRig.Camera.DriverId, _activeRig.Name);
                }
            }
        }
        finally
        {
            _stateLock.Release();
        }

        return result;
    }

    private async Task<Result<bool>> InitializeDriverInternalAsync(ICameraAdapter driver, CancellationToken cancellationToken)
    {
        var initResult = await driver.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (initResult.IsFailure)
        {
            if (initResult.Error is not null)
            {
                _logger?.LogError(initResult.Error, "Camera driver initialization failed for rig {RigName}.", _activeRig.Name);
            }
            else
            {
                _logger?.LogError("Camera driver initialization failed for rig {RigName} with unknown error.", _activeRig.Name);
            }

            await ResetDriverAsync(driver, logErrors: false).ConfigureAwait(false);
            return initResult;
        }

        return Result<bool>.Success(true);
    }

    private async Task ShutdownDriverAsync()
    {
        ICameraAdapter? driver;

        await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            driver = _driver;
            _driverInitialized = false;
            _initializationTask = null;
        }
        finally
        {
            _stateLock.Release();
        }

        if (driver is not null)
        {
            await ResetDriverAsync(driver, logErrors: true).ConfigureAwait(false);
        }
    }

    private async Task ResetDriverAsync(ICameraAdapter driver, bool logErrors)
    {
        try
        {
            var shutdownResult = await driver.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            if (shutdownResult.IsFailure && logErrors)
            {
                _logger?.LogWarning(shutdownResult.Error, "Camera driver shutdown reported an error for rig {RigName}.", _activeRig.Name);
            }
        }
        catch (Exception ex) when (logErrors)
        {
            _logger?.LogWarning(ex, "Camera driver shutdown threw an exception for rig {RigName}.", _activeRig.Name);
        }
        finally
        {
            await driver.DisposeAsync().ConfigureAwait(false);
        }

        await _stateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_driver, driver))
            {
                _driver = null;
                _driverInitialized = false;
                _initializationTask = null;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private RigSpec ResolveActiveRig()
    {
        var result = _rigCatalog.ResolveActive();
        if (result.IsFailure)
        {
            throw result.Error ?? new InvalidOperationException("Active rig could not be resolved from the catalog.");
        }

        return result.Value;
    }

    private Task ReloadFromCatalogAsync()
    {
        return Task.Run(async () =>
        {
            try
            {
                var resolved = _rigCatalog.ResolveActive();
                if (resolved.IsFailure)
                {
                    _logger?.LogWarning(resolved.Error, "Failed to resolve active rig from catalog during change notification.");
                    return;
                }

                var newRig = resolved.Value;
                if (string.Equals(newRig.Name, _activeRig.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var reload = await ReloadAsync(newRig, CancellationToken.None).ConfigureAwait(false);
                if (reload.IsFailure)
                {
                    _logger?.LogError(reload.Error, "Catalog change reload failed for rig {RigName}.", newRig.Name);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unhandled exception while reloading rig acquisition adapter after catalog change.");
            }
        });
    }

    private enum AdapterState
    {
        Stopped = 0,
        Running,
        Paused
    }
}
