using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class CameraConfigurationTab : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    private IReadOnlyList<OpticsCatalogCamera> _cameras = Array.Empty<OpticsCatalogCamera>();
    private IReadOnlyList<OpticsRigSummary> _rigs = Array.Empty<OpticsRigSummary>();
    private IReadOnlyDictionary<string, IReadOnlyList<OpticsRigSummary>> _cameraUsage = new Dictionary<string, IReadOnlyList<OpticsRigSummary>>(StringComparer.OrdinalIgnoreCase);

    private bool _isLoading;
    private string? _errorMessage;
    private string? _lastUpdatedMessage;
    private string? _activeRigKey;

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public ILogger<CameraConfigurationTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private int CameraCount => _cameras.Count;

    private int RigCount => _rigs.Count;

    private int UniqueManufacturerCount => _cameras
        .Select(camera => camera.Manufacturer)
        .Where(manufacturer => !string.IsNullOrWhiteSpace(manufacturer))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    private string ActiveRigCameraDisplay
    {
        get
        {
            var activeRig = ResolveActiveRig();
            if (activeRig is null)
            {
                return "Not assigned";
            }

            var camera = _cameras.FirstOrDefault(cam => string.Equals(cam.Key, activeRig.CameraKey, StringComparison.OrdinalIgnoreCase));
            return camera is null ? "Not assigned" : camera.DisplayName;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadCatalogAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _lifetime.Cancel();
        }

        _lifetime.Dispose();
    }

    private Task ReloadAsync()
        => LoadCatalogAsync();

    private async Task LoadCatalogAsync()
    {
        _isLoading = true;
        _errorMessage = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var response = await LocalApiClient.GetOpticsCatalogAsync(CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "Unable to load camera catalog from the local API.";
                return;
            }

            ApplyCatalog(response);
            _lastUpdatedMessage = $"Updated {DateTimeOffset.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load camera catalog data.");
            _errorMessage = ex.Message;
        }
        finally
        {
            _isLoading = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private void ApplyCatalog(OpticsCatalogResponse response)
    {
        _cameras = response.Cameras.OrderBy(camera => camera.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _rigs = response.Rigs.OrderBy(rig => rig.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _activeRigKey = response.ActiveRigKey;

        var usage = _rigs
            .GroupBy(rig => rig.CameraKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<OpticsRigSummary>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        _cameraUsage = usage;
    }

    private IReadOnlyList<OpticsRigSummary> GetCameraUsage(string cameraKey)
    {
        if (_cameraUsage.TryGetValue(cameraKey, out var rigs))
        {
            return rigs;
        }

        return Array.Empty<OpticsRigSummary>();
    }

    private string GetCameraUsageSummary(string cameraKey)
    {
        var rigs = GetCameraUsage(cameraKey);
        if (rigs.Count == 0)
        {
            return "Not assigned to any rig.";
        }

        return string.Join(", ", rigs.Select(rig => rig.DisplayName));
    }

    private string FormatResolution(OpticsCatalogCamera camera)
        => $"{camera.SensorWidthPixels:N0} × {camera.SensorHeightPixels:N0} px";

    private OpticsRigSummary? ResolveActiveRig()
    {
        if (_rigs.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_activeRigKey))
        {
            var active = _rigs.FirstOrDefault(rig => string.Equals(rig.Key, _activeRigKey, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                return active;
            }
        }

        return _rigs.FirstOrDefault(rig => rig.IsActive) ?? _rigs.First();
    }

    private async Task RequestRepaintAsync()
    {
        try
        {
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
