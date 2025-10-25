using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Models.Catalog;
using HVO.SkyMonitorV5.RPi.Models.Cameras;
using HVO.SkyMonitorV5.RPi.Models.Rigs;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class CameraConfigurationTab : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<CameraCatalogItem> _filteredCameras = new();

    private IReadOnlyList<CameraCatalogItem> _cameras = Array.Empty<CameraCatalogItem>();
    private IReadOnlyList<RigSummary> _rigs = Array.Empty<RigSummary>();
    private IReadOnlyDictionary<string, IReadOnlyList<RigSummary>> _cameraUsage = new Dictionary<string, IReadOnlyList<RigSummary>>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CameraDriverDescriptorResponse> _driverDescriptors = Array.Empty<CameraDriverDescriptorResponse>();

    private int? _selectedCameraId;
    private CameraEditModel? _editModel;
    private CameraEditModel? _baseline;
    private EditContext? _editContext;

    private bool _isLoading;
    private bool _isSaving;
    private bool _hasChanges;
    private string? _errorMessage;
    private string? _successMessage;
    private string? _lastUpdatedMessage;
    private bool _catalogCollapsed;
    private bool _telemetryCollapsed;
    private bool _editorCollapsed;
    private string _searchText = string.Empty;

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    [Inject]
    public ILogger<CameraConfigurationTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private bool IsBusy => _isLoading || _isSaving;

    private bool CanReloadCatalog => !_isLoading && !_isSaving;

    private IReadOnlyList<CameraCatalogItem> FilteredCameras => _filteredCameras;

    private string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? string.Empty;
            if (!string.Equals(_searchText, next, StringComparison.Ordinal))
            {
                _searchText = next;
                ApplyFilter();
            }
        }
    }

    private CameraCatalogItem? SelectedCamera
    {
        get
        {
            if (_selectedCameraId is int selectedId)
            {
                var match = _cameras.FirstOrDefault(camera => camera.Id == selectedId);
                if (match is not null)
                {
                    return match;
                }
            }

            if (_editModel is { Id: > 0 } model)
            {
                return _cameras.FirstOrDefault(camera => camera.Id == model.Id);
            }

            return null;
        }
    }

    private bool CanSave => _editModel is not null && !_isLoading && !_isSaving && _hasChanges;

    protected override async Task OnInitializedAsync()
        => await LoadCatalogAsync().ConfigureAwait(false);

    public void Dispose()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _lifetime.Cancel();
        }

        _lifetime.Dispose();
    }

    private Task ReloadAsync()
        => CanReloadCatalog ? LoadCatalogAsync() : Task.CompletedTask;

    private async Task LoadCatalogAsync()
    {
        _isLoading = true;
        _errorMessage = null;
        _successMessage = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var catalogResult = (await LocalApiClient.GetEquipmentCatalogAsync(CancellationToken).ConfigureAwait(false))
                .ToResult("Unable to load camera catalog from the local API.");

            if (catalogResult.IsFailure)
            {
                Logger?.LogWarning(catalogResult.Error, "Unable to load camera catalog from the local API.");
                _cameras = Array.Empty<CameraCatalogItem>();
                _rigs = Array.Empty<RigSummary>();
                _cameraUsage = new Dictionary<string, IReadOnlyList<RigSummary>>(StringComparer.OrdinalIgnoreCase);
                _filteredCameras.Clear();
                _selectedCameraId = null;
                _errorMessage = catalogResult.Error?.Message ?? "Unable to load camera catalog from the local API.";
                return;
            }

            ApplyCatalog(catalogResult.Value);
            await LoadDriverCatalogAsync().ConfigureAwait(false);
            _lastUpdatedMessage = $"Updated {DateTimeOffset.Now:HH:mm:ss}";

            var preferred = ResolveSelectionAfterRefresh();
            if (preferred is not null)
            {
                SelectCamera(preferred);
            }
            else
            {
                ClearSelection();
            }
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

    private void ApplyCatalog(EquipmentCatalogResponse response)
    {
        _cameras = response.Cameras
            .OrderBy(camera => camera.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _rigs = response.Rigs
            .OrderBy(rig => rig.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cameraUsage = _rigs
            .GroupBy(rig => rig.CameraKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RigSummary>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        if (_selectedCameraId is int selectedId && _cameras.All(camera => camera.Id != selectedId))
        {
            _selectedCameraId = null;
        }

        ApplyFilter();
    }

    private async Task LoadDriverCatalogAsync()
    {
        try
        {
            var driverResult = (await LocalApiClient.GetCameraDriverCatalogAsync(CancellationToken).ConfigureAwait(false))
                .ToResult("The local API returned no camera driver descriptors.");

            if (driverResult.IsFailure)
            {
                Logger?.LogWarning(driverResult.Error, "Local API returned no camera driver descriptors.");
                _driverDescriptors = Array.Empty<CameraDriverDescriptorResponse>();
                return;
            }

            ApplyDriverCatalog(driverResult.Value);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to load camera driver descriptors from the local API.");
            _driverDescriptors = Array.Empty<CameraDriverDescriptorResponse>();
        }
    }

    private void ApplyDriverCatalog(CameraDriverCatalogResponse response)
    {
        _driverDescriptors = response.Drivers
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyFilter()
    {
        _filteredCameras.Clear();

        if (_cameras.Count == 0)
        {
            return;
        }

        var filter = _searchText?.Trim();
        IEnumerable<CameraCatalogItem> query = _cameras;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(camera =>
                camera.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || camera.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || camera.Manufacturer.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || camera.Model.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || camera.DriverId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || camera.DriverVersion.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || camera.AdapterName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || camera.AdditionalTags.Any(tag => tag.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var camera in query)
        {
            _filteredCameras.Add(camera);
        }

        if (_filteredCameras.Count == 0)
        {
            return;
        }

        if (_selectedCameraId is int selectedId
            && _filteredCameras.All(camera => camera.Id != selectedId)
            && !_hasChanges)
        {
            var replacement = _filteredCameras[0];
            _selectedCameraId = replacement.Id;

            if (!_isLoading && !_isSaving)
            {
                SelectCamera(replacement);
            }
        }
    }

    private CameraCatalogItem? ResolveSelectionAfterRefresh()
    {
        if (_cameras.Count == 0)
        {
            return null;
        }

        if (_selectedCameraId is int selectedId)
        {
            var match = _cameras.FirstOrDefault(camera => camera.Id == selectedId);
            if (match is not null)
            {
                return match;
            }
        }

        if (_editModel is not null)
        {
            var existing = _cameras.FirstOrDefault(camera => camera.Id == _editModel.Id);
            if (existing is not null)
            {
                return existing;
            }
        }

        return _cameras.First();
    }

    private void SelectCamera(CameraCatalogItem camera)
    {
        _errorMessage = null;
        _successMessage = null;
        _selectedCameraId = camera.Id;

        var model = CameraEditModel.FromCatalog(camera);
        AttachEditContext(model);
    }

    private void AttachEditContext(CameraEditModel model)
    {
        DetachEditContext();

        _editModel = model;
        _baseline = model.Clone();
        _editContext = new EditContext(model);
        _editContext.OnFieldChanged += HandleFieldChanged;
        _hasChanges = false;
    }

    private void DetachEditContext()
    {
        if (_editContext is not null)
        {
            _editContext.OnFieldChanged -= HandleFieldChanged;
        }

        _editContext = null;
        _editModel = null;
        _baseline = null;
        _hasChanges = false;
    }

    private void ClearSelection()
    {
        _selectedCameraId = null;
        DetachEditContext();
    }

    private void ResetChanges()
    {
        if (_baseline is null)
        {
            return;
        }

        var snapshot = _baseline.Clone();
        AttachEditContext(snapshot);
    }

    private void HandleRowSelection(CameraCatalogItem camera)
    {
        if (_isSaving)
        {
            return;
        }

        if (_hasChanges && _editModel is not null && camera.Id != _editModel.Id)
        {
            _errorMessage = "Save or reset your changes before switching cameras.";
            return;
        }

        SelectCamera(camera);
    }

    private void HandleFieldChanged(object? sender, FieldChangedEventArgs args)
    {
        if (_editModel is null || _baseline is null)
        {
            return;
        }

        _hasChanges = !_editModel.EqualsByValue(_baseline);
    }

    private string GetToolbarStatus()
    {
        if (!string.IsNullOrWhiteSpace(_errorMessage))
        {
            return "Camera catalog failed to load.";
        }

        if (_isSaving)
        {
            return "Saving camera catalog changes…";
        }

        if (_isLoading)
        {
            return "Loading camera catalog…";
        }

        if (_cameras.Count == 0)
        {
            return "No cameras registered.";
        }

        if (_filteredCameras.Count == _cameras.Count || string.IsNullOrWhiteSpace(_searchText))
        {
            return FormattableString.Invariant($"{_cameras.Count} camera{(_cameras.Count == 1 ? string.Empty : "s")} registered.");
        }

        return FormattableString.Invariant($"{_filteredCameras.Count} of {_cameras.Count} cameras match the filter.");
    }

    private string GetToolbarStatusCss()
    {
        if (!string.IsNullOrWhiteSpace(_errorMessage))
        {
            return "text-danger";
        }

        if (_isSaving || _isLoading)
        {
            return "text-muted";
        }

        return "text-muted";
    }

    private string GetEmptyFilterMessage()
    {
        if (_cameras.Count == 0)
        {
            return _isLoading ? "Loading camera catalog…" : "No cameras have been registered yet.";
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return "No cameras are available.";
        }

        return "No cameras match the current filter.";
    }

    private void ToggleCatalogSection()
    {
        _catalogCollapsed = !_catalogCollapsed;
    }

    private void ToggleTelemetrySection()
    {
        _telemetryCollapsed = !_telemetryCollapsed;
    }

    private void ToggleEditorSection()
    {
        _editorCollapsed = !_editorCollapsed;
    }

    private static string GetCollapseIconCss(bool collapsed)
        => collapsed ? "bi bi-chevron-down" : "bi bi-chevron-up";

    private static string GetCollapseCss(bool collapsed)
        => collapsed ? "collapse-hidden" : string.Empty;

    private static string GetCollapseButtonTitle(string sectionName, bool collapsed)
        => collapsed ? $"Expand {sectionName}" : $"Collapse {sectionName}";

    private CameraDriverDescriptorResponse? ResolveDriverDescriptor(string? driverId)
    {
        if (string.IsNullOrWhiteSpace(driverId) || _driverDescriptors.Count == 0)
        {
            return null;
        }

        return _driverDescriptors.FirstOrDefault(descriptor => string.Equals(descriptor.Id, driverId, StringComparison.OrdinalIgnoreCase));
    }

    private string GetDriverSettingsHelpText()
    {
        if (_editModel is null)
        {
            return "Provide driver-specific JSON payloads when required.";
        }

        var descriptor = ResolveDriverDescriptor(_editModel.DriverId);
        if (descriptor is null)
        {
            return "Driver registry did not return metadata for this identifier. Leave blank unless the driver expects configuration.";
        }

        if (!descriptor.SupportsConfiguration)
        {
            return $"{descriptor.DisplayName} does not declare a configuration payload; leave blank to use driver defaults.";
        }

        var typeName = descriptor.ConfigurationType ?? descriptor.AssemblyQualifiedName ?? "configuration type";
        return $"Provide JSON matching {typeName}. Leave blank to fall back to driver defaults.";
    }

    private async Task SaveAsync()
    {
        if (_editContext is null || _editModel is null)
        {
            return;
        }

        _errorMessage = null;
        _successMessage = null;

        if (!_editContext.Validate())
        {
            _errorMessage = "Please resolve validation errors before saving.";
            await RequestRepaintAsync().ConfigureAwait(false);
            return;
        }

        _isSaving = true;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            Result<EquipmentCatalogResponse> result;

            if (_editModel.Id == 0)
            {
                result = (await LocalApiClient.CreateCameraAsync(_editModel.ToCreateRequest(), CancellationToken).ConfigureAwait(false))
                    .ToResult("The local API did not return updated camera data.");
            }
            else
            {
                result = (await LocalApiClient.UpdateCameraAsync(_editModel.Id, _editModel.ToUpdateRequest(), CancellationToken).ConfigureAwait(false))
                    .ToResult("The local API did not return updated camera data.");
            }

            if (result.IsFailure)
            {
                _errorMessage = result.Error?.Message ?? "The local API did not return updated camera data.";
                return;
            }

            ApplyCatalog(result.Value);

            CameraCatalogItem? refreshed = _editModel.Id == 0
                ? _cameras.FirstOrDefault(camera => string.Equals(camera.Key, _editModel.Key, StringComparison.OrdinalIgnoreCase))
                : _cameras.FirstOrDefault(camera => camera.Id == _editModel.Id);

            if (refreshed is not null)
            {
                SelectCamera(refreshed);
            }
            else
            {
                ClearSelection();
            }

            _successMessage = "Camera catalog entry saved.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to persist camera catalog changes.");
            _errorMessage = ex.Message;
        }
        finally
        {
            _isSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private IReadOnlyList<RigSummary> GetCameraUsage(string cameraKey)
    {
        if (_cameraUsage.TryGetValue(cameraKey, out var rigs))
        {
            return rigs;
        }

        return Array.Empty<RigSummary>();
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

    private static string FormatResolution(CameraCatalogItem camera)
        => $"{camera.SensorWidthPixels:N0} × {camera.SensorHeightPixels:N0} px";

    private static string GetStatusBadgeCss(bool isActive)
        => isActive
            ? "badge bg-success-subtle text-success-emphasis align-self-start"
            : "badge bg-secondary-subtle text-secondary-emphasis align-self-start";

    private static string GetStatusLabel(bool isActive)
        => isActive ? "Active" : "Disabled";

    private static string GetUsageBadgeCss(bool isInUse)
        => isInUse
            ? "badge bg-info-subtle text-info-emphasis align-self-start"
            : "badge bg-secondary-subtle text-secondary-emphasis align-self-start";

    private static string GetUsageBadgeText(int usageCount)
        => usageCount > 0 ? $"{usageCount} rig(s)" : "Not assigned";

    private string FormatLifecycleLabel(DateTime updatedUtc)
    {
        if (updatedUtc == default)
        {
            return "Updated —";
        }

        var relative = FormatRelativeTimestamp(updatedUtc);
        var absolute = FormatAbsoluteTimestamp(updatedUtc);
        return FormattableString.Invariant($"Updated {relative} ({absolute})");
    }

    private static string GetLifecycleTitle(CameraCatalogItem camera)
        => FormattableString.Invariant($"Created {FormatAbsoluteTimestamp(camera.CreatedUtc)} | Updated {FormatAbsoluteTimestamp(camera.UpdatedUtc)}");

    private string FormatAuditTimestamp(DateTime timestamp)
    {
        if (timestamp == default)
        {
            return "—";
        }

        var absolute = FormatAbsoluteTimestamp(timestamp);
        var relative = FormatRelativeTimestamp(timestamp);
        return FormattableString.Invariant($"{absolute} ({relative})");
    }

    private string GetLifecycleSummary(CameraCatalogItem camera)
        => FormattableString.Invariant($"{(camera.IsActive ? "Active" : "Disabled")} · {GetCameraUsageSummary(camera.Key)}");

    private static string GetSensorSummary(CameraCatalogItem camera)
        => FormattableString.Invariant($"{camera.SensorWidthPixels:N0}×{camera.SensorHeightPixels:N0}px · {camera.PixelSizeMicrons:F2} µm · {(!string.IsNullOrWhiteSpace(camera.SensorTechnology) ? camera.SensorTechnology : "Sensor technology unspecified")}");

    private static string GetColorSummary(CameraCatalogItem camera)
    {
        if (string.IsNullOrWhiteSpace(camera.ColorMode))
        {
            return "Color mode not specified.";
        }

        return FormattableString.Invariant($"{camera.ColorMode} sensor");
    }

    private static string GetCoolingSummary(CameraCatalogItem camera)
    {
        if (string.IsNullOrWhiteSpace(camera.Cooling))
        {
            return "Cooling profile not specified.";
        }

        return camera.Cooling;
    }

    private IReadOnlyList<string> GetCapabilityLabels(CameraCatalogItem camera)
    {
        var labels = new List<string>();

        if (camera.SupportsGainControl)
        {
            labels.Add("Gain Control");
        }

        if (camera.SupportsExposureControl)
        {
            labels.Add("Exposure Control");
        }

        if (camera.SupportsTemperatureTelemetry)
        {
            labels.Add("Temperature Telemetry");
        }

        if (camera.SupportsSoftwareBinning)
        {
            labels.Add("Software Binning");
        }

        if (camera.IsSynthetic)
        {
            labels.Add("Synthetic Profile");
        }

        if (!string.IsNullOrWhiteSpace(camera.Cooling) && !string.Equals(camera.Cooling, "None", StringComparison.OrdinalIgnoreCase))
        {
            labels.Add(FormattableString.Invariant($"{camera.Cooling} Cooling"));
        }

        return labels.Count == 0 ? Array.Empty<string>() : labels;
    }

    private IReadOnlyList<string> GetAdditionalTags(CameraCatalogItem camera)
    {
        if (camera.AdditionalTags.Count == 0)
        {
            return Array.Empty<string>();
        }

        return camera.AdditionalTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag) && !tag.StartsWith("CoolingTargetCelsius:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string? GetCoolingTargetLabel(CameraCatalogItem camera)
    {
        foreach (var tag in camera.AdditionalTags)
        {
            if (!tag.StartsWith("CoolingTargetCelsius:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = tag.Substring("CoolingTargetCelsius:".Length);
            if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return FormattableString.Invariant($"{value:F1} °C target");
            }
        }

        return null;
    }

    private string FormatRelativeTimestamp(DateTime timestamp)
    {
        if (timestamp == default)
        {
            return "—";
        }

        var utc = EnsureUtc(timestamp);
        var local = utc.ToLocalTime();
        var now = TimeProvider.GetLocalNow();
        var diff = now - new DateTimeOffset(local);

        if (diff < TimeSpan.Zero)
        {
            diff = TimeSpan.Zero;
        }

        return diff switch
        {
            { TotalSeconds: < 1 } => "just now",
            { TotalMinutes: < 1 } => FormattableString.Invariant($"{diff.Seconds}s ago"),
            { TotalHours: < 1 } => FormattableString.Invariant($"{(int)diff.TotalMinutes}m ago"),
            { TotalDays: < 1 } => FormattableString.Invariant($"{(int)diff.TotalHours}h ago"),
            { TotalDays: < 7 } => FormattableString.Invariant($"{(int)diff.TotalDays}d ago"),
            _ => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }

    private static string FormatAbsoluteTimestamp(DateTime timestamp)
    {
        if (timestamp == default)
        {
            return "—";
        }

        var utc = EnsureUtc(timestamp);
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static DateTime EnsureUtc(DateTime timestamp)
        => timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };

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

    private sealed class CameraEditModel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        [RegularExpression("^[A-Za-z0-9_-]+$")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Manufacturer { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Model { get; set; } = string.Empty;

        [MaxLength(64)]
        public string DriverVersion { get; set; } = string.Empty;

        [MaxLength(128)]
        public string AdapterName { get; set; } = string.Empty;

        [MaxLength(128)]
        public string DriverId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string SyntheticProfile { get; set; } = string.Empty;

        public string DriverSettingsJson { get; set; } = string.Empty;

        public bool IsSynthetic { get; set; }

        [Range(1, 20000)]
        public int SensorWidthPixels { get; set; } = 1;

        [Range(1, 20000)]
        public int SensorHeightPixels { get; set; } = 1;

        [Range(0.1, 100.0)]
        public double PixelSizeMicrons { get; set; } = 1.0;

        [Range(0.0, 20000.0)]
        public double? SensorCxPixels { get; set; }

        [Range(0.0, 20000.0)]
        public double? SensorCyPixels { get; set; }

        public CameraColorMode ColorMode { get; set; } = CameraColorMode.Color;

        public bool IsColor
        {
            get => ColorMode is CameraColorMode.Color or CameraColorMode.Switchable;
            set => ColorMode = value
                ? (ColorMode == CameraColorMode.Switchable ? CameraColorMode.Switchable : CameraColorMode.Color)
                : CameraColorMode.Monochrome;
        }

        public CameraSensorTechnology SensorTechnology { get; set; } = CameraSensorTechnology.Cmos;

        [MaxLength(64)]
        public string BodyType { get; set; } = string.Empty;

        public CameraCoolingType CoolingType { get; set; } = CameraCoolingType.None;

        public bool HasCooling
        {
            get => CoolingType != CameraCoolingType.None;
            set
            {
                CoolingType = value
                    ? (CoolingType == CameraCoolingType.None ? CameraCoolingType.Regulated : CoolingType)
                    : CameraCoolingType.None;

                if (!value)
                {
                    CoolingTargetCelsius = null;
                }
            }
        }

        [Range(-120.0, 60.0)]
        public double? CoolingTargetCelsius { get; set; }

        public bool SupportsGainControl { get; set; } = true;

        public bool SupportsExposureControl { get; set; } = true;

        public bool SupportsTemperatureTelemetry { get; set; }

        public bool SupportsSoftwareBinning { get; set; }

        public bool IsActive { get; set; } = true;

        public long Revision { get; set; }

        public string AdditionalTagsInput { get; set; } = string.Empty;

        public CreateCameraRequest ToCreateRequest()
            => new()
            {
                Key = Key.Trim(),
                DisplayName = DisplayName.Trim(),
                Manufacturer = Manufacturer.Trim(),
                Model = Model.Trim(),
                DriverVersion = DriverVersion.Trim(),
                AdapterName = AdapterName.Trim(),
                DriverId = DriverId.Trim(),
                SyntheticProfile = SyntheticProfile.Trim(),
                IsSynthetic = IsSynthetic,
                SensorWidthPixels = SensorWidthPixels,
                SensorHeightPixels = SensorHeightPixels,
                PixelSizeMicrons = PixelSizeMicrons,
                SensorCxPixels = SensorCxPixels,
                SensorCyPixels = SensorCyPixels,
                ColorMode = ColorMode.ToString(),
                SensorTechnology = SensorTechnology.ToString(),
                BodyType = BodyType.Trim(),
                Cooling = CoolingType.ToString(),
                SupportsGainControl = SupportsGainControl,
                SupportsExposureControl = SupportsExposureControl,
                SupportsTemperatureTelemetry = SupportsTemperatureTelemetry,
                SupportsSoftwareBinning = SupportsSoftwareBinning,
                IsActive = IsActive,
                AdditionalTags = BuildAdditionalTags(),
                DriverSettingsJson = string.IsNullOrWhiteSpace(DriverSettingsJson) ? null : DriverSettingsJson
            };

        public UpdateCameraRequest ToUpdateRequest()
        {
            var request = new UpdateCameraRequest
            {
                Revision = Revision,
                Key = Key,
                DisplayName = DisplayName,
                Manufacturer = Manufacturer,
                Model = Model,
                DriverVersion = DriverVersion,
                AdapterName = AdapterName,
                DriverId = DriverId,
                SyntheticProfile = SyntheticProfile,
                IsSynthetic = IsSynthetic,
                SensorWidthPixels = SensorWidthPixels,
                SensorHeightPixels = SensorHeightPixels,
                PixelSizeMicrons = PixelSizeMicrons,
                SensorCxPixels = SensorCxPixels,
                SensorCyPixels = SensorCyPixels,
                ColorMode = ColorMode.ToString(),
                SensorTechnology = SensorTechnology.ToString(),
                BodyType = BodyType,
                Cooling = CoolingType.ToString(),
                SupportsGainControl = SupportsGainControl,
                SupportsExposureControl = SupportsExposureControl,
                SupportsTemperatureTelemetry = SupportsTemperatureTelemetry,
                SupportsSoftwareBinning = SupportsSoftwareBinning,
                IsActive = IsActive,
                AdditionalTags = BuildAdditionalTags(),
                DriverSettingsJson = string.IsNullOrWhiteSpace(DriverSettingsJson) ? null : DriverSettingsJson
            };

            return request;
        }

        public CameraEditModel Clone()
            => new()
            {
                Id = Id,
                Key = Key,
                DisplayName = DisplayName,
                Manufacturer = Manufacturer,
                Model = Model,
                DriverVersion = DriverVersion,
                AdapterName = AdapterName,
                DriverId = DriverId,
                SyntheticProfile = SyntheticProfile,
                IsSynthetic = IsSynthetic,
                SensorWidthPixels = SensorWidthPixels,
                SensorHeightPixels = SensorHeightPixels,
                PixelSizeMicrons = PixelSizeMicrons,
                SensorCxPixels = SensorCxPixels,
                SensorCyPixels = SensorCyPixels,
                ColorMode = ColorMode,
                SensorTechnology = SensorTechnology,
                BodyType = BodyType,
                CoolingType = CoolingType,
                CoolingTargetCelsius = CoolingTargetCelsius,
                SupportsGainControl = SupportsGainControl,
                SupportsExposureControl = SupportsExposureControl,
                SupportsTemperatureTelemetry = SupportsTemperatureTelemetry,
                SupportsSoftwareBinning = SupportsSoftwareBinning,
                IsActive = IsActive,
                Revision = Revision,
                AdditionalTagsInput = AdditionalTagsInput,
                DriverSettingsJson = DriverSettingsJson
            };

        public bool EqualsByValue(CameraEditModel other)
        {
            if (other is null)
            {
                return false;
            }

            return Id == other.Id
                && string.Equals(Key, other.Key, StringComparison.Ordinal)
                && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
                && string.Equals(Manufacturer, other.Manufacturer, StringComparison.Ordinal)
                && string.Equals(Model, other.Model, StringComparison.Ordinal)
                && string.Equals(DriverVersion, other.DriverVersion, StringComparison.Ordinal)
                && string.Equals(AdapterName, other.AdapterName, StringComparison.Ordinal)
                && string.Equals(DriverId, other.DriverId, StringComparison.Ordinal)
                && string.Equals(SyntheticProfile, other.SyntheticProfile, StringComparison.Ordinal)
                && string.Equals(DriverSettingsJson ?? string.Empty, other.DriverSettingsJson ?? string.Empty, StringComparison.Ordinal)
                && IsSynthetic == other.IsSynthetic
                && SensorWidthPixels == other.SensorWidthPixels
                && SensorHeightPixels == other.SensorHeightPixels
                && Math.Abs(PixelSizeMicrons - other.PixelSizeMicrons) < 0.0001
                && Nullable.Equals(SensorCxPixels, other.SensorCxPixels)
                && Nullable.Equals(SensorCyPixels, other.SensorCyPixels)
                && ColorMode == other.ColorMode
                && SensorTechnology == other.SensorTechnology
                && string.Equals(BodyType, other.BodyType, StringComparison.Ordinal)
                && CoolingType == other.CoolingType
                && NearlyEquals(CoolingTargetCelsius, other.CoolingTargetCelsius)
                && SupportsGainControl == other.SupportsGainControl
                && SupportsExposureControl == other.SupportsExposureControl
                && SupportsTemperatureTelemetry == other.SupportsTemperatureTelemetry
                && SupportsSoftwareBinning == other.SupportsSoftwareBinning
                && IsActive == other.IsActive
                && Revision == other.Revision
                && string.Equals(AdditionalTagsInput, other.AdditionalTagsInput, StringComparison.Ordinal);
        }

        public static CameraEditModel FromCatalog(CameraCatalogItem item)
        {
            var model = new CameraEditModel
            {
                Id = item.Id,
                Key = item.Key,
                DisplayName = item.DisplayName,
                Manufacturer = item.Manufacturer,
                Model = item.Model,
                DriverVersion = item.DriverVersion,
                AdapterName = item.AdapterName,
                DriverId = item.DriverId,
                SyntheticProfile = item.SyntheticProfile ?? string.Empty,
                IsSynthetic = item.IsSynthetic,
                SensorWidthPixels = item.SensorWidthPixels,
                SensorHeightPixels = item.SensorHeightPixels,
                PixelSizeMicrons = item.PixelSizeMicrons,
                SensorCxPixels = item.SensorCxPixels,
                SensorCyPixels = item.SensorCyPixels,
                BodyType = item.BodyType,
                SupportsGainControl = item.SupportsGainControl,
                SupportsExposureControl = item.SupportsExposureControl,
                SupportsTemperatureTelemetry = item.SupportsTemperatureTelemetry,
                SupportsSoftwareBinning = item.SupportsSoftwareBinning,
                IsActive = item.IsActive,
                Revision = item.Revision,
                DriverSettingsJson = item.DriverSettingsJson ?? string.Empty
            };

            if (Enum.TryParse(item.ColorMode, ignoreCase: true, out CameraColorMode colorMode))
            {
                model.ColorMode = colorMode;
            }

            if (Enum.TryParse(item.SensorTechnology, ignoreCase: true, out CameraSensorTechnology sensorTechnology))
            {
                model.SensorTechnology = sensorTechnology;
            }

            if (Enum.TryParse(item.Cooling, ignoreCase: true, out CameraCoolingType coolingType))
            {
                model.CoolingType = coolingType;
            }

            var manualTags = new List<string>();
            double? coolingTarget = null;

            foreach (var tag in item.AdditionalTags)
            {
                if (TryParseCoolingTarget(tag, out var parsedTarget))
                {
                    coolingTarget = parsedTarget;
                    continue;
                }

                manualTags.Add(tag);
            }

            model.CoolingTargetCelsius = coolingTarget;
            model.AdditionalTagsInput = manualTags.Count == 0 ? string.Empty : string.Join(Environment.NewLine, manualTags);

            return model;
        }
        private IReadOnlyList<string> BuildAdditionalTags()
        {
            var tokens = string.IsNullOrWhiteSpace(AdditionalTagsInput)
                ? new List<string>()
                : AdditionalTagsInput
                    .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(tag => tag.Trim())
                    .Where(tag => tag.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var filtered = tokens
                .Where(tag => !tag.StartsWith("CoolingTargetCelsius:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (CoolingTargetCelsius is double target)
            {
                filtered.Add($"CoolingTargetCelsius:{target.ToString("F1", CultureInfo.InvariantCulture)}");
            }

            return filtered;
        }

        private static bool NearlyEquals(double? left, double? right)
        {
            if (!left.HasValue && !right.HasValue)
            {
                return true;
            }

            if (!left.HasValue || !right.HasValue)
            {
                return false;
            }

            return Math.Abs(left.Value - right.Value) < 0.0001;
        }

        private static bool TryParseCoolingTarget(string tag, out double value)
        {
            value = 0;

            if (!tag.StartsWith("CoolingTargetCelsius:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var payload = tag.Substring("CoolingTargetCelsius:".Length);
            return double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
