using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Models.Adapters;
using HVO.SkyMonitorV5.RPi.Models.Catalog;
using HVO.SkyMonitorV5.RPi.Models.Rigs;
using HVO.SkyMonitorV5.RPi.Options;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class AdapterConfigurationTab : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    private IReadOnlyList<AdapterSummary> _adapters = Array.Empty<AdapterSummary>();
    private IReadOnlyList<RigSummary> _rigs = Array.Empty<RigSummary>();

    private EditContext? _editContext;
    private AdapterEditModel? _editModel;
    private AdapterEditModel? _baseline;

    private bool _isLoading;
    private bool _isSaving;
    private bool _hasChanges;
    private bool _hasRigOptions;
    private string? _errorMessage;
    private string? _successMessage;

    private static readonly IReadOnlyList<AdapterTypeOption> AdapterTypeOptions = new[]
    {
        new AdapterTypeOption(CameraAdapterTypes.Mock, "Simulated (Mono)", "Synthetic monochrome adapter ideal for testing capture pipelines."),
        new AdapterTypeOption(CameraAdapterTypes.MockColor, "Simulated (Color)", "Synthetic colour adapter that mirrors ASI174MC characteristics."),
        new AdapterTypeOption(CameraAdapterTypes.Zwo, "ZWO (Native)", "Native ZWO adapter for production camera control.")
    };

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public ILogger<AdapterConfigurationTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private bool HasSelection => _editModel is not null;

    private bool IsNewSelection => _editModel is { Id: 0 };

    private bool CanSave => HasSelection && _hasRigOptions && !_isLoading && !_isSaving && _hasChanges;

    private bool CanDelete => HasSelection && !IsNewSelection && !_isLoading && !_isSaving;

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
            var response = await LocalApiClient.GetEquipmentCatalogAsync(CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "Unable to load adapter catalog from the local API.";
                return;
            }

            ApplyCatalog(response);

            var preferred = ResolveSelectionAfterRefresh();
            if (preferred is not null)
            {
                SelectAdapter(preferred);
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
            Logger?.LogError(ex, "Failed to load adapter configuration catalog.");
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
        _adapters = response.Adapters
            .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _rigs = response.Rigs
            .OrderBy(rig => rig.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _hasRigOptions = _rigs.Count > 0;
    }

    private AdapterSummary? ResolveSelectionAfterRefresh()
    {
        if (_adapters.Count == 0)
        {
            return null;
        }

        if (_editModel is not null)
        {
            var existing = _adapters.FirstOrDefault(adapter => adapter.Id == _editModel.Id);
            if (existing is not null)
            {
                return existing;
            }

            if (!string.IsNullOrWhiteSpace(_editModel.Name))
            {
                var matchingByName = _adapters.FirstOrDefault(adapter => string.Equals(adapter.Name, _editModel.Name, StringComparison.OrdinalIgnoreCase));
                if (matchingByName is not null)
                {
                    return matchingByName;
                }
            }
        }

        return _adapters.First();
    }

    private void SelectAdapter(AdapterSummary adapter)
    {
        _errorMessage = null;
        _successMessage = null;

        var model = AdapterEditModel.FromSummary(adapter);
        AttachEditContext(model);
    }

    private void BeginCreate()
    {
        if (!_hasRigOptions)
        {
            _errorMessage = "Create a rig before adding adapters.";
            return;
        }

        _errorMessage = null;
        _successMessage = null;

        var model = AdapterEditModel.CreateNew(_rigs.FirstOrDefault()?.Key);
        AttachEditContext(model);
    }

    private void AttachEditContext(AdapterEditModel model)
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

    private void HandleFieldChanged(object? sender, FieldChangedEventArgs args)
    {
        if (_editModel is null || _baseline is null)
        {
            return;
        }

        _hasChanges = !_editModel.EqualsByValue(_baseline);
    }

    private async Task ReloadAsync()
    {
        if (_isLoading)
        {
            return;
        }

        await LoadCatalogAsync().ConfigureAwait(false);
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

        if (!_hasRigOptions)
        {
            _errorMessage = "Adapters require a rig binding. Add a rig before saving.";
            await RequestRepaintAsync().ConfigureAwait(false);
            return;
        }

        _isSaving = true;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            EquipmentCatalogResponse? response;

            if (_editModel.Id == 0)
            {
                response = await LocalApiClient.CreateAdapterAsync(_editModel.ToCreateRequest(), CancellationToken).ConfigureAwait(false);
            }
            else
            {
                response = await LocalApiClient.UpdateAdapterAsync(_editModel.Id, _editModel.ToUpdateRequest(), CancellationToken).ConfigureAwait(false);
            }

            if (response is null)
            {
                _errorMessage = "The local API did not return updated adapter data.";
                return;
            }

            ApplyCatalog(response);
            _successMessage = "Adapter configuration saved.";

            AdapterSummary? selected;
            if (_editModel.Id == 0)
            {
                selected = _adapters.FirstOrDefault(adapter => string.Equals(adapter.Name, _editModel.Name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                selected = _adapters.FirstOrDefault(adapter => adapter.Id == _editModel.Id);
            }

            if (selected is not null)
            {
                SelectAdapter(selected);
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
            Logger?.LogError(ex, "Failed to persist adapter configuration changes.");
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
            var response = await LocalApiClient.DeleteAdapterAsync(_editModel.Id, CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "The local API did not confirm deletion.";
                return;
            }

            ApplyCatalog(response);
            _successMessage = "Adapter removed.";

            ClearSelection();

            if (_adapters.Count > 0)
            {
                SelectAdapter(_adapters.First());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to delete adapter configuration {AdapterId}.", _editModel?.Id);
            _errorMessage = ex.Message;
        }
        finally
        {
            _isSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private async Task ConfirmDeleteAdapterAsync(AdapterSummary adapter)
    {
        if (_isSaving || _isLoading)
        {
            return;
        }

        SelectAdapter(adapter);
        await DeleteAsync().ConfigureAwait(false);
    }

    private void HandleRowSelection(AdapterSummary adapter)
    {
        if (_isSaving)
        {
            return;
        }

        if (_hasChanges && _editModel is not null && adapter.Id != _editModel.Id)
        {
            _errorMessage = "Save or discard your changes before switching adapters.";
            return;
        }

        SelectAdapter(adapter);
    }

    private string GetAdapterTypeLabel(string adapterType)
    {
        var option = AdapterTypeOptions.FirstOrDefault(value => string.Equals(value.Value, adapterType, StringComparison.OrdinalIgnoreCase));
        return option?.Label ?? adapterType;
    }

    private string GetAdapterTypeDescription(string adapterType)
    {
        var option = AdapterTypeOptions.FirstOrDefault(value => string.Equals(value.Value, adapterType, StringComparison.OrdinalIgnoreCase));
        return option?.Description ?? "";
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

    private sealed class AdapterEditModel
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(128)]
        [RegularExpression("^[A-Za-z0-9_-]+$")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string AdapterType { get; set; } = CameraAdapterTypes.Mock;

        [Required]
        [MaxLength(128)]
        public string RigKey { get; set; } = string.Empty;

        public static AdapterEditModel CreateNew(string? defaultRigKey)
            => new()
            {
                Id = 0,
                Name = string.Empty,
                AdapterType = CameraAdapterTypes.Mock,
                RigKey = defaultRigKey ?? string.Empty
            };

        public static AdapterEditModel FromSummary(AdapterSummary summary)
            => new()
            {
                Id = summary.Id,
                Name = summary.Name,
                AdapterType = summary.AdapterType,
                RigKey = summary.RigKey
            };

        public AdapterEditModel Clone()
            => new()
            {
                Id = Id,
                Name = Name,
                AdapterType = AdapterType,
                RigKey = RigKey
            };

        public CreateAdapterRequest ToCreateRequest()
            => new()
            {
                Name = Name?.Trim() ?? string.Empty,
                AdapterType = AdapterType?.Trim() ?? string.Empty,
                RigKey = RigKey?.Trim() ?? string.Empty
            };

        public UpdateAdapterRequest ToUpdateRequest()
            => new()
            {
                Name = Name?.Trim() ?? string.Empty,
                AdapterType = AdapterType?.Trim() ?? string.Empty,
                RigKey = RigKey?.Trim() ?? string.Empty
            };

        public bool EqualsByValue(AdapterEditModel other)
        {
            if (other is null)
            {
                return false;
            }

            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                && string.Equals(AdapterType, other.AdapterType, StringComparison.Ordinal)
                && string.Equals(RigKey, other.RigKey, StringComparison.Ordinal);
        }
    }

    private sealed record AdapterTypeOption(string Value, string Label, string Description);
}
