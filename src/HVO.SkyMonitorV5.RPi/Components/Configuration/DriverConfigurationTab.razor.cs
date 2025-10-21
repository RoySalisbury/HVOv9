using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class DriverConfigurationTab : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<CameraDriverDescriptorResponse> _filteredDrivers = new();

    private IReadOnlyList<CameraDriverDescriptorResponse> _drivers = Array.Empty<CameraDriverDescriptorResponse>();
    private string? _selectedDriverId;
    private string? _errorMessage;
    private bool _isLoading;
    private bool _catalogCollapsed;
    private bool _detailsCollapsed;
    private bool _lifecycleCollapsed;
    private string _searchText = string.Empty;

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public ILogger<DriverConfigurationTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private bool IsBusy => _isLoading;

    private bool CanReloadCatalog => !_isLoading;

    private string SearchText
    {
        get => _searchText;
        set
        {
            if (!string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                _searchText = value ?? string.Empty;
                ApplyFilter();
            }
        }
    }

    private CameraDriverDescriptorResponse? SelectedDriver
        => string.IsNullOrWhiteSpace(_selectedDriverId)
            ? null
            : _drivers.FirstOrDefault(driver => string.Equals(driver.Id, _selectedDriverId, StringComparison.OrdinalIgnoreCase));

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

    private async Task ReloadCatalogAsync()
    {
        if (!CanReloadCatalog)
        {
            return;
        }

        await LoadCatalogAsync().ConfigureAwait(false);
    }

    private async Task LoadCatalogAsync()
    {
        _isLoading = true;
        _errorMessage = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var response = await LocalApiClient.GetCameraDriverCatalogAsync(CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "Unable to load the driver catalog from the local API.";
                _drivers = Array.Empty<CameraDriverDescriptorResponse>();
                ApplyFilter();
                return;
            }

            ApplyCatalog(response);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load driver catalog.");
            _errorMessage = ex.Message;
            _drivers = Array.Empty<CameraDriverDescriptorResponse>();
            ApplyFilter();
        }
        finally
        {
            _isLoading = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private void ApplyCatalog(CameraDriverCatalogResponse response)
    {
        if (response?.Drivers is null)
        {
            _drivers = Array.Empty<CameraDriverDescriptorResponse>();
        }
        else
        {
            _drivers = response.Drivers
                .OrderBy(driver => driver.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(driver => driver.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(_selectedDriverId)
            && _drivers.All(driver => !string.Equals(driver.Id, _selectedDriverId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedDriverId = null;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _filteredDrivers.Clear();

        if (_drivers.Count == 0)
        {
            _selectedDriverId = null;
            return;
        }

        var filter = _searchText?.Trim();

        IEnumerable<CameraDriverDescriptorResponse> query = _drivers;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = _drivers.Where(driver =>
                driver.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || driver.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(driver.Version) && driver.Version.Contains(filter, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(driver.Description) && driver.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(driver.ConfigurationType) && driver.ConfigurationType.Contains(filter, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(driver.AssemblyQualifiedName) && driver.AssemblyQualifiedName.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }

        _filteredDrivers.AddRange(query);

        if (_filteredDrivers.Count == 0)
        {
            _selectedDriverId = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedDriverId)
            || _filteredDrivers.All(driver => !string.Equals(driver.Id, _selectedDriverId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedDriverId = _filteredDrivers[0].Id;
        }
    }

    private string GetToolbarStatus()
    {
        if (!string.IsNullOrWhiteSpace(_errorMessage))
        {
            return "Driver catalog failed to load.";
        }

        if (_drivers.Count == 0)
        {
            return IsBusy ? "Loading driver catalog…" : "No drivers discovered.";
        }

        if (_filteredDrivers.Count == _drivers.Count)
        {
            return $"{_drivers.Count} driver{(_drivers.Count == 1 ? string.Empty : "s")} discovered.";
        }

        return $"{_filteredDrivers.Count} of {_drivers.Count} drivers match the filter.";
    }

    private string GetEmptyFilterMessage()
    {
        if (_drivers.Count == 0)
        {
            return IsBusy ? "Loading driver catalog…" : "No driver providers have been discovered yet.";
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return "No drivers are available.";
        }

        return "No drivers match the current filter.";
    }

    private void ToggleCatalogSection()
    {
        _catalogCollapsed = !_catalogCollapsed;
    }

    private void ToggleDetailsSection()
    {
        _detailsCollapsed = !_detailsCollapsed;
    }

    private void ToggleLifecycleSection()
    {
        _lifecycleCollapsed = !_lifecycleCollapsed;
    }

    private static string GetCollapseIconCss(bool collapsed)
        => collapsed ? "bi bi-chevron-down" : "bi bi-chevron-up";

    private static string GetCollapseCss(bool collapsed)
        => collapsed ? "collapse-hidden" : string.Empty;

    private static string GetCollapseButtonTitle(string sectionName, bool collapsed)
        => collapsed ? $"Expand {sectionName}" : $"Collapse {sectionName}";

    private void SelectDriver(string driverId)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            return;
        }

        if (_filteredDrivers.Any(driver => string.Equals(driver.Id, driverId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedDriverId = driverId;
        }
    }

    private static string GetConfigurationSupportLabel(CameraDriverDescriptorResponse driver)
    {
        if (driver.ConfigurationType is null)
        {
            return driver.SupportsConfiguration
                ? "Supports configuration via custom schema."
                : "No configuration schema is defined.";
        }

        return driver.SupportsConfiguration
            ? $"Configuration type: {driver.ConfigurationType}."
            : $"Configuration type {driver.ConfigurationType} is present but marked as unsupported.";
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
