using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO;
using HVO.SkyMonitorV5.RPi.Cameras.Optics;
using HVO.SkyMonitorV5.RPi.Cameras.Rendering;
using HVO.SkyMonitorV5.RPi.Models.Catalog;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Models.Rigs;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class OpticsConfigurationTab : ComponentBase, IDisposable
{
    private static readonly ProjectionModel[] ProjectionModelValues = Enum.GetValues<ProjectionModel>();
    private static readonly LensKind[] LensKindValues = Enum.GetValues<LensKind>();

    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<OpticsCatalogItem> _filteredOptics = new();

    private IReadOnlyList<OpticsCatalogItem> _optics = Array.Empty<OpticsCatalogItem>();
    private IReadOnlyList<RigSummary> _rigs = Array.Empty<RigSummary>();
    private IReadOnlyDictionary<string, IReadOnlyList<RigSummary>> _opticsUsage = new Dictionary<string, IReadOnlyList<RigSummary>>(StringComparer.OrdinalIgnoreCase);

    private OpticsEditModel? _editModel;
    private OpticsEditModel? _baseline;
    private EditContext? _editContext;

    private bool _isLoading;
    private bool _isSaving;
    private bool _hasChanges;
    private bool _catalogCollapsed;
    private string? _errorMessage;
    private string? _successMessage;
    private string? _lastUpdatedMessage;
    private string? _activeRigKey;
    private string _searchText = string.Empty;

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public ILogger<OpticsConfigurationTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private bool IsBusy => _isLoading || _isSaving;

    private bool CanReloadCatalog => !_isLoading && !_isSaving;

    private bool CanSave => _editModel is not null && !_isLoading && !_isSaving && _hasChanges;

    private IReadOnlyList<ProjectionModel> ProjectionModels => ProjectionModelValues;

    private IReadOnlyList<LensKind> LensKinds => LensKindValues;

    private IReadOnlyList<OpticsCatalogItem> FilteredOptics => _filteredOptics;

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
                _ = RequestRepaintAsync();
            }
        }
    }

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
                .ToResult("Unable to load optics catalog from the local API.");

            if (catalogResult.IsFailure)
            {
                Logger?.LogWarning(catalogResult.Error, "Unable to load optics catalog from the local API.");
                _optics = Array.Empty<OpticsCatalogItem>();
                _rigs = Array.Empty<RigSummary>();
                _opticsUsage = new Dictionary<string, IReadOnlyList<RigSummary>>(StringComparer.OrdinalIgnoreCase);
                _filteredOptics.Clear();
                DetachEditContext();
                _errorMessage = catalogResult.Error?.Message ?? "Unable to load optics catalog from the local API.";
                return;
            }

            ApplyCatalog(catalogResult.Value);
            _lastUpdatedMessage = $"Updated {DateTimeOffset.Now:HH:mm:ss}";

            var preferred = ResolveSelectionAfterRefresh();
            if (preferred is not null)
            {
                SelectOptics(preferred);
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
            Logger?.LogError(ex, "Failed to load optics catalog data.");
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
        _optics = response.Optics
            .OrderBy(optics => optics.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _rigs = response.Rigs
            .OrderBy(rig => rig.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _activeRigKey = response.ActiveRigKey;

        _opticsUsage = _rigs
            .GroupBy(rig => rig.OpticsKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RigSummary>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _filteredOptics.Clear();

        if (_optics.Count == 0)
        {
            return;
        }

        var filter = _searchText.Trim();
        IEnumerable<OpticsCatalogItem> query = _optics;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(optics =>
                optics.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || optics.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || optics.ProjectionModel.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || optics.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var optics in query)
        {
            _filteredOptics.Add(optics);
        }
    }

    private string GetToolbarStatus()
    {
        if (!string.IsNullOrWhiteSpace(_errorMessage))
        {
            return "Optics catalog failed to load.";
        }

        if (_isSaving)
        {
            return "Saving optics catalog changes…";
        }

        if (_isLoading)
        {
            return "Loading optics catalog…";
        }

        if (_optics.Count == 0)
        {
            return "No optics entries registered.";
        }

        if (_filteredOptics.Count == _optics.Count || string.IsNullOrWhiteSpace(_searchText))
        {
            return FormattableString.Invariant($"{_optics.Count} optics entr{(_optics.Count == 1 ? "y" : "ies")} loaded.");
        }

        return FormattableString.Invariant($"{_filteredOptics.Count} of {_optics.Count} optics entries match the filter.");
    }

    private string GetToolbarStatusCss()
        => !string.IsNullOrWhiteSpace(_errorMessage) ? "text-danger" : "text-muted";

    private string GetEmptyFilterMessage()
    {
        if (_optics.Count == 0)
        {
            return _isLoading ? "Loading optics catalog…" : "No optics entries are registered yet.";
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return "No optics entries are available.";
        }

        return "No optics entries match the current filter.";
    }

    private void ToggleCatalogSection()
    {
        _catalogCollapsed = !_catalogCollapsed;
        StateHasChanged();
    }

    private static string GetCollapseIconCss(bool collapsed)
        => collapsed ? "bi bi-chevron-down" : "bi bi-chevron-up";

    private static string GetCollapseCss(bool collapsed)
        => collapsed ? "collapse-hidden" : string.Empty;

    private static string GetCollapseButtonTitle(string sectionName, bool collapsed)
        => collapsed ? $"Expand {sectionName}" : $"Collapse {sectionName}";

    private OpticsCatalogItem? ResolveSelectionAfterRefresh()
    {
        if (_optics.Count == 0)
        {
            return null;
        }

        if (_editModel is not null)
        {
            var existing = _optics.FirstOrDefault(optics => optics.Id == _editModel.Id);
            if (existing is not null)
            {
                return existing;
            }
        }

        return _optics.First();
    }

    private void SelectOptics(OpticsCatalogItem optics)
    {
        _errorMessage = null;
        _successMessage = null;

        var model = OpticsEditModel.FromCatalog(optics);
        AttachEditContext(model);
    }

    private void AttachEditContext(OpticsEditModel model)
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
        => DetachEditContext();

    private void ResetChanges()
    {
        if (_baseline is null)
        {
            return;
        }

        var snapshot = _baseline.Clone();
        AttachEditContext(snapshot);
    }

    private void HandleRowSelection(OpticsCatalogItem optics)
    {
        if (_isSaving)
        {
            return;
        }

        if (_hasChanges && _editModel is not null && optics.Id != _editModel.Id)
        {
            _errorMessage = "Save or reset your changes before switching optics.";
            return;
        }

        SelectOptics(optics);
    }

    private void HandleFieldChanged(object? sender, FieldChangedEventArgs args)
    {
        if (_editModel is null || _baseline is null)
        {
            return;
        }

        _hasChanges = !_editModel.EqualsByValue(_baseline);
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
                result = (await LocalApiClient.CreateOpticsAsync(_editModel.ToCreateRequest(), CancellationToken).ConfigureAwait(false))
                    .ToResult("The local API did not return updated optics data.");
            }
            else
            {
                result = (await LocalApiClient.UpdateOpticsAsync(_editModel.Id, _editModel.ToUpdateRequest(), CancellationToken).ConfigureAwait(false))
                    .ToResult("The local API did not return updated optics data.");
            }

            if (result.IsFailure)
            {
                _errorMessage = result.Error?.Message ?? "The local API did not return updated optics data.";
                return;
            }

            ApplyCatalog(result.Value);

            OpticsCatalogItem? refreshed = _editModel.Id == 0
                ? _optics.FirstOrDefault(optics => string.Equals(optics.Key, _editModel.Key, StringComparison.OrdinalIgnoreCase))
                : _optics.FirstOrDefault(optics => optics.Id == _editModel.Id);

            if (refreshed is not null)
            {
                SelectOptics(refreshed);
            }
            else
            {
                ClearSelection();
            }

            _successMessage = "Optics catalog entry saved.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to persist optics catalog changes.");
            _errorMessage = ex.Message;
        }
        finally
        {
            _isSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private IReadOnlyList<RigSummary> GetOpticsUsage(string opticsKey)
    {
        if (_opticsUsage.TryGetValue(opticsKey, out var rigs))
        {
            return rigs;
        }

        return Array.Empty<RigSummary>();
    }

    private string GetRigUsageSummary(string opticsKey)
    {
        var rigs = GetOpticsUsage(opticsKey);
        if (rigs.Count == 0)
        {
            return "Not assigned to any rig.";
        }

        return string.Join(", ", rigs.Select(rig => rig.DisplayName));
    }

    private string FormatFocalLength(double value)
        => $"{value:F1} mm";

    private string FormatFieldOfView(double value)
        => $"{value:F1}°";

    private string FormatOptionalFieldOfView(double? value)
        => value.HasValue ? $"{value.Value:F1}°" : "—";

    private string FormatBoresight(RigSummary rig)
        => $"{rig.BoresightAltitudeDegrees:F1}° / {rig.BoresightAzimuthDegrees:F1}°";

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

    private static string FormatLifecycleLabel(DateTime updatedUtc)
    {
        var formatted = FormatTimestamp(updatedUtc);
        return formatted == "—" ? "Updated —" : $"Updated {formatted}";
    }

    private static string GetLifecycleTitle(OpticsCatalogItem optics)
        => $"Created {FormatTimestamp(optics.CreatedUtc)} | Updated {FormatTimestamp(optics.UpdatedUtc)}";

    private static string FormatTimestamp(DateTime timestamp)
    {
        if (timestamp == default)
        {
            return "—";
        }

        var utc = timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };

        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string GetProjectionLabel(ProjectionModel projection)
        => projection switch
        {
            ProjectionModel.EquisolidAngle => "Equisolid Angle",
            _ => projection.ToString()
        };

    private static string GetLensKindLabel(LensKind kind)
        => kind switch
        {
            LensKind.Rectilinear => "Rectilinear",
            LensKind.Telescope => "Telescope",
            _ => kind.ToString()
        };

    private static string GetProjectionLabel(string? projection)
    {
        if (Enum.TryParse(projection, ignoreCase: true, out ProjectionModel parsed))
        {
            return GetProjectionLabel(parsed);
        }

        return string.IsNullOrWhiteSpace(projection) ? "Unknown" : projection;
    }

    private static string GetLensKindLabel(string? kind)
    {
        if (Enum.TryParse(kind, ignoreCase: true, out LensKind parsed))
        {
            return GetLensKindLabel(parsed);
        }

        return string.IsNullOrWhiteSpace(kind) ? "Unknown" : kind;
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

    private sealed class OpticsEditModel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        [RegularExpression("^[A-Za-z0-9_-]+$")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string DisplayName { get; set; } = string.Empty;

        public ProjectionModel ProjectionModel { get; set; } = ProjectionModel.Perspective;

        [Range(0.1, 1000.0)]
        public double FocalLengthMillimeters { get; set; } = 1.0;

        [Range(0.1, 360.0)]
        public double FieldOfViewXDegrees { get; set; } = 1.0;

        [Range(0.0, 360.0)]
        public double? FieldOfViewYDegrees { get; set; }

        [Range(-180.0, 180.0)]
        public double RollDegrees { get; set; }

        public LensKind Kind { get; set; } = LensKind.Rectilinear;

        public bool IsActive { get; set; } = true;

        public long Revision { get; set; }

        public Models.Optics.CreateOpticsRequest ToCreateRequest()
        {
            var request = new Models.Optics.CreateOpticsRequest();
            request.Key = this.Key.Trim();
            request.DisplayName = this.DisplayName.Trim();
            request.ProjectionModel = this.ProjectionModel.ToString();
            request.FocalLengthMillimeters = this.FocalLengthMillimeters;
            request.FieldOfViewXDegrees = this.FieldOfViewXDegrees;
            request.FieldOfViewYDegrees = this.FieldOfViewYDegrees;
            request.RollDegrees = this.RollDegrees;
            request.Kind = this.Kind.ToString();
            request.IsActive = this.IsActive;
            return request;
        }

        public Models.Optics.UpdateOpticsRequest ToUpdateRequest()
        {
            var request = new Models.Optics.UpdateOpticsRequest();
            request.Revision = this.Revision;
            request.Key = this.Key;
            request.DisplayName = this.DisplayName;
            request.ProjectionModel = this.ProjectionModel.ToString();
            request.FocalLengthMillimeters = this.FocalLengthMillimeters;
            request.FieldOfViewXDegrees = this.FieldOfViewXDegrees;
            request.FieldOfViewYDegrees = this.FieldOfViewYDegrees;
            request.RollDegrees = this.RollDegrees;
            request.Kind = this.Kind.ToString();
            request.IsActive = this.IsActive;
            return request;
        }

        public OpticsEditModel Clone()
            => new()
            {
                Id = Id,
                Key = Key,
                DisplayName = DisplayName,
                ProjectionModel = ProjectionModel,
                FocalLengthMillimeters = FocalLengthMillimeters,
                FieldOfViewXDegrees = FieldOfViewXDegrees,
                FieldOfViewYDegrees = FieldOfViewYDegrees,
                RollDegrees = RollDegrees,
                Kind = Kind,
                IsActive = IsActive,
                Revision = Revision
            };

        public bool EqualsByValue(OpticsEditModel other)
        {
            if (other is null)
            {
                return false;
            }

            return Id == other.Id
                && string.Equals(Key, other.Key, StringComparison.Ordinal)
                && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
                && ProjectionModel == other.ProjectionModel
                && Math.Abs(FocalLengthMillimeters - other.FocalLengthMillimeters) < 0.0001
                && NearlyEquals(FieldOfViewXDegrees, other.FieldOfViewXDegrees)
                && NearlyEquals(FieldOfViewYDegrees, other.FieldOfViewYDegrees)
                && Math.Abs(RollDegrees - other.RollDegrees) < 0.0001
                && Kind == other.Kind
                && IsActive == other.IsActive
                && Revision == other.Revision;
        }

        public static OpticsEditModel FromCatalog(OpticsCatalogItem item)
        {
            var model = new OpticsEditModel
            {
                Id = item.Id,
                Key = item.Key,
                DisplayName = item.DisplayName,
                FocalLengthMillimeters = item.FocalLengthMillimeters,
                FieldOfViewXDegrees = item.FieldOfViewXDegrees,
                FieldOfViewYDegrees = item.FieldOfViewYDegrees,
                RollDegrees = item.RollDegrees,
                IsActive = item.IsActive,
                Revision = item.Revision
            };

            if (Enum.TryParse(item.ProjectionModel, ignoreCase: true, out ProjectionModel parsedProjection))
            {
                model.ProjectionModel = parsedProjection;
            }

            if (Enum.TryParse(item.Kind, ignoreCase: true, out LensKind parsedKind))
            {
                model.Kind = parsedKind;
            }

            return model;
        }
        private static bool NearlyEquals(double left, double right)
            => Math.Abs(left - right) < 0.0001;

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
    }
}
