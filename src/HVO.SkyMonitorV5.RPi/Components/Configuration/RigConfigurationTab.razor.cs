using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models.Optics;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class RigConfigurationTab : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    private IReadOnlyList<OpticsRigSummary> _rigs = Array.Empty<OpticsRigSummary>();
    private IReadOnlyList<OpticsCatalogCamera> _cameras = Array.Empty<OpticsCatalogCamera>();
    private IReadOnlyList<OpticsCatalogLens> _lenses = Array.Empty<OpticsCatalogLens>();
    private string? _activeRigKey;

    private EditContext? _editContext;
    private RigEditModel? _editModel;
    private RigEditModel? _baseline;

    private bool _isLoading;
    private bool _isSaving;
    private bool _hasChanges;
    private string? _errorMessage;
    private string? _successMessage;

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public ILogger<RigConfigurationTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private bool HasSelection => _editModel is not null;

    private bool IsNewSelection => _editModel is { Id: 0 };

    private bool CanSave => HasSelection && !_isLoading && !_isSaving && _hasChanges;

    private bool CanDelete => HasSelection && !IsNewSelection && !_isLoading && !_isSaving && _editModel is { IsActive: false } model && !HasAdapterBindings(model.Id);

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

    private async Task LoadCatalogAsync()
    {
        _isLoading = true;
        _errorMessage = null;
        _successMessage = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var response = await LocalApiClient.GetOpticsCatalogAsync(CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "Unable to load rig catalog from the local API.";
                return;
            }

            ApplyCatalog(response);

            var preferred = ResolveSelectionAfterRefresh();
            if (preferred is not null)
            {
                SelectRig(preferred);
            }
            else
            {
                ClearSelection();
            }
        }
        catch (OperationCanceledException)
        {
            // component disposed
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load rig configuration catalog.");
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
        _rigs = response.Rigs.OrderBy(rig => rig.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _cameras = response.Cameras.OrderBy(camera => camera.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _lenses = response.Lenses.OrderBy(lens => lens.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _activeRigKey = response.ActiveRigKey;
    }

    private void SelectRig(OpticsRigSummary rig)
    {
        _errorMessage = null;
        _successMessage = null;

        var model = RigEditModel.FromSummary(rig);
        if (rig.IsActive)
        {
            _activeRigKey = rig.Key;
        }
        AttachEditContext(model);
    }

    private void BeginCreate()
    {
        _errorMessage = null;
        _successMessage = null;

        var model = RigEditModel.CreateNew(
            _cameras.FirstOrDefault()?.Key,
            _lenses.FirstOrDefault()?.Key,
            _rigs.Count == 0);
        AttachEditContext(model);
    }

    private void AttachEditContext(RigEditModel model)
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
        DetachEditContext();
    }

    private OpticsRigSummary? ResolveSelectionAfterRefresh()
    {
        if (_rigs.Count == 0)
        {
            return null;
        }

        if (_editModel is not null)
        {
            var existing = _rigs.FirstOrDefault(rig => rig.Id == _editModel.Id);
            if (existing is not null)
            {
                return existing;
            }
        }

        if (!string.IsNullOrWhiteSpace(_activeRigKey))
        {
            var active = _rigs.FirstOrDefault(rig => string.Equals(rig.Key, _activeRigKey, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                return active;
            }
        }

        return _rigs.First();
    }

    private async Task SaveAsync()
    {
        if (_editContext is null || _editModel is null)
        {
            return;
        }

        _errorMessage = null;
        _successMessage = null;

        var isValid = _editContext.Validate();
        if (!isValid)
        {
            _errorMessage = "Please resolve validation errors before saving.";
            await RequestRepaintAsync().ConfigureAwait(false);
            return;
        }

        _isSaving = true;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            OpticsCatalogResponse? response;

            if (_editModel.Id == 0)
            {
                response = await LocalApiClient.CreateOpticsRigAsync(_editModel.ToCreateRequest(), CancellationToken).ConfigureAwait(false);
            }
            else
            {
                response = await LocalApiClient.UpdateOpticsRigAsync(_editModel.Id, _editModel.ToUpdateRequest(), CancellationToken).ConfigureAwait(false);
            }

            if (response is null)
            {
                _errorMessage = "The local API did not return updated rig data.";
                return;
            }

            ApplyCatalog(response);
            _successMessage = "Rig configuration saved.";

            if (_editModel.Id == 0)
            {
                var created = _rigs.FirstOrDefault(rig => string.Equals(rig.Key, _editModel.Key, StringComparison.OrdinalIgnoreCase));
                if (created is not null)
                {
                    SelectRig(created);
                }
                else
                {
                    ClearSelection();
                }
            }
            else
            {
                var updated = _rigs.FirstOrDefault(rig => rig.Id == _editModel.Id);
                if (updated is not null)
                {
                    SelectRig(updated);
                }
                else
                {
                    ClearSelection();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to persist rig configuration changes.");
            _errorMessage = ex.Message;
        }
        finally
        {
            _isSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private async Task DeleteAsync()
    {
        if (_editModel is null || _editModel.Id == 0)
        {
            return;
        }

        _errorMessage = null;
        _successMessage = null;
        _isSaving = true;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var response = await LocalApiClient.DeleteOpticsRigAsync(_editModel.Id, _editModel.Revision, CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "The local API did not confirm deletion.";
                return;
            }

            ApplyCatalog(response);
            _successMessage = "Rig configuration removed.";

            DetachEditContext();

            if (_rigs.Count > 0)
            {
                SelectRig(_rigs.First());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to delete rig configuration {RigId}.", _editModel?.Id);
            _errorMessage = ex.Message;
        }
        finally
        {
            _isSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private async Task SetActiveAsync(OpticsRigSummary rig, bool isActive)
    {
        if (_isSaving || _isLoading)
        {
            return;
        }

        try
        {
            var request = new UpdateOpticsRigRequest
            {
                Revision = rig.Revision,
                DisplayName = rig.DisplayName,
                CameraKey = rig.CameraKey,
                LensKey = rig.LensKey,
                BoresightAltitudeDegrees = rig.BoresightAltitudeDegrees,
                BoresightAzimuthDegrees = rig.BoresightAzimuthDegrees,
                IsActive = isActive
            };

            _isSaving = true;
            await RequestRepaintAsync().ConfigureAwait(false);

            var response = await LocalApiClient.UpdateOpticsRigAsync(rig.Id, request, CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "The local API did not update the rig activation state.";
                return;
            }

            ApplyCatalog(response);

            var refreshed = _rigs.FirstOrDefault(entry => entry.Id == rig.Id);
            if (refreshed is not null)
            {
                if (_editModel is not null && _editModel.Id == refreshed.Id)
                {
                    SelectRig(refreshed);
                }
            }

            _successMessage = isActive
                ? $"'{rig.DisplayName}' is now active."
                : $"'{rig.DisplayName}' has been disabled.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to toggle rig activation for {RigId}.", rig.Id);
            _errorMessage = ex.Message;
        }
        finally
        {
            _isSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private bool HasAdapterBindings(int rigId)
        => _rigs.FirstOrDefault(rig => rig.Id == rigId)?.HasAdapterBindings ?? false;

    private void HandleRowSelection(OpticsRigSummary rig)
    {
        if (_isSaving)
        {
            return;
        }

        if (_hasChanges && _editModel is not null && rig.Id != _editModel.Id)
        {
            // Preserve unsaved state; require explicit button when switching away with edits.
            _errorMessage = "Save or discard your changes before switching rigs.";
            return;
        }

        SelectRig(rig);
    }

    private void HandleFieldChanged(object? sender, FieldChangedEventArgs args)
    {
        if (_editModel is null || _baseline is null)
        {
            return;
        }

        _hasChanges = !_editModel.EqualsByValue(_baseline);
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

    private sealed class RigEditModel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        [RegularExpression("^[A-Za-z0-9_-]+$")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string CameraKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string LensKey { get; set; } = string.Empty;

        [Range(0.0, 90.0)]
        public double BoresightAltitudeDegrees { get; set; } = 90.0;

        [Range(0.0, 360.0)]
        public double BoresightAzimuthDegrees { get; set; } = 0.0;

        public bool IsActive { get; set; }

        public long Revision { get; set; }

        public bool HasAdapterBindings { get; set; }

        public static RigEditModel CreateNew(string? defaultCameraKey, string? defaultLensKey, bool isActiveByDefault)
            => new()
            {
                Id = 0,
                Key = string.Empty,
                DisplayName = string.Empty,
                CameraKey = defaultCameraKey ?? string.Empty,
                LensKey = defaultLensKey ?? string.Empty,
                BoresightAltitudeDegrees = 90.0,
                BoresightAzimuthDegrees = 0.0,
                IsActive = isActiveByDefault,
                Revision = 0,
                HasAdapterBindings = false
            };

        public static RigEditModel FromSummary(OpticsRigSummary summary)
            => new()
            {
                Id = summary.Id,
                Key = summary.Key,
                DisplayName = summary.DisplayName,
                CameraKey = summary.CameraKey,
                LensKey = summary.LensKey,
                BoresightAltitudeDegrees = summary.BoresightAltitudeDegrees,
                BoresightAzimuthDegrees = summary.BoresightAzimuthDegrees,
                IsActive = summary.IsActive,
                Revision = summary.Revision,
                HasAdapterBindings = summary.HasAdapterBindings
            };

        public RigEditModel Clone()
            => new()
            {
                Id = Id,
                Key = Key,
                DisplayName = DisplayName,
                CameraKey = CameraKey,
                LensKey = LensKey,
                BoresightAltitudeDegrees = BoresightAltitudeDegrees,
                BoresightAzimuthDegrees = BoresightAzimuthDegrees,
                IsActive = IsActive,
                Revision = Revision,
                HasAdapterBindings = HasAdapterBindings
            };

        public CreateOpticsRigRequest ToCreateRequest()
            => new()
            {
                Key = Key?.Trim() ?? string.Empty,
                DisplayName = DisplayName?.Trim() ?? string.Empty,
                CameraKey = CameraKey?.Trim() ?? string.Empty,
                LensKey = LensKey?.Trim() ?? string.Empty,
                BoresightAltitudeDegrees = BoresightAltitudeDegrees,
                BoresightAzimuthDegrees = BoresightAzimuthDegrees,
                IsActive = IsActive
            };

        public UpdateOpticsRigRequest ToUpdateRequest()
            => new()
            {
                Revision = Revision,
                DisplayName = DisplayName?.Trim() ?? string.Empty,
                CameraKey = CameraKey?.Trim() ?? string.Empty,
                LensKey = LensKey?.Trim() ?? string.Empty,
                BoresightAltitudeDegrees = BoresightAltitudeDegrees,
                BoresightAzimuthDegrees = BoresightAzimuthDegrees,
                IsActive = IsActive
            };

        public bool EqualsByValue(RigEditModel other)
        {
            if (other is null)
            {
                return false;
            }

            return string.Equals(Key, other.Key, StringComparison.Ordinal)
                && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
                && string.Equals(CameraKey, other.CameraKey, StringComparison.Ordinal)
                && string.Equals(LensKey, other.LensKey, StringComparison.Ordinal)
                && Math.Abs(BoresightAltitudeDegrees - other.BoresightAltitudeDegrees) < 0.0001
                && Math.Abs(BoresightAzimuthDegrees - other.BoresightAzimuthDegrees) < 0.0001
                && IsActive == other.IsActive;
        }
    }
}
