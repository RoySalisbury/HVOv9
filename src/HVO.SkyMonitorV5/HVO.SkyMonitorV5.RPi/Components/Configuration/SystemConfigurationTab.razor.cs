using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.Core.Results;
using HVO.SkyMonitorV5.RPi.Models.System;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class SystemConfigurationTab : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    private EditContext? _observatoryEditContext;
    private EditContext? _localApiEditContext;
    private EditContext? _telemetryEditContext;

    private UpdateSystemObservatoryRequest _observatoryEdit = new();
    private UpdateSystemObservatoryRequest? _observatoryBaseline;
    private SystemObservatoryConfigurationResponse? _observatorySnapshot;
    private bool _observatoryIsLoading;
    private bool _observatoryIsSaving;
    private bool _observatoryHasChanges;
    private bool _observatoryCollapsed;
    private string? _observatoryError;
    private string? _observatorySuccessMessage;

    private UpdateSystemLocalApiRequest _localApiEdit = new();
    private UpdateSystemLocalApiRequest? _localApiBaseline;
    private SystemLocalApiConfigurationResponse? _localApiSnapshot;
    private bool _localApiIsLoading;
    private bool _localApiIsSaving;
    private bool _localApiHasChanges;
    private bool _localApiCollapsed;
    private string? _localApiError;
    private string? _localApiSuccessMessage;

    private UpdateSystemTelemetryRetentionRequest _telemetryEdit = new();
    private UpdateSystemTelemetryRetentionRequest? _telemetryBaseline;
    private SystemTelemetryRetentionConfigurationResponse? _telemetrySnapshot;
    private bool _telemetryIsLoading;
    private bool _telemetryIsSaving;
    private bool _telemetryHasChanges;
    private bool _telemetryCollapsed;
    private string? _telemetryError;
    private string? _telemetrySuccessMessage;

    private IReadOnlyList<TimeZoneInfo> _timeZones = Array.Empty<TimeZoneInfo>();

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    [Inject]
    public ILogger<SystemConfigurationTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private bool HasObservatoryChanges => _observatoryBaseline is not null && _observatoryHasChanges;
    private bool CanSaveObservatory => _observatoryEditContext is not null && !_observatoryIsLoading && !_observatoryIsSaving && HasObservatoryChanges;
    private bool CanCancelObservatory => !_observatoryIsLoading && !_observatoryIsSaving && HasObservatoryChanges;
    private bool CanReloadObservatory => !_observatoryIsLoading && !_observatoryIsSaving;

    private bool HasLocalApiChanges => _localApiBaseline is not null && _localApiHasChanges;
    private bool CanSaveLocalApi => _localApiEditContext is not null && !_localApiIsLoading && !_localApiIsSaving && HasLocalApiChanges;
    private bool CanCancelLocalApi => !_localApiIsLoading && !_localApiIsSaving && HasLocalApiChanges;
    private bool CanReloadLocalApi => !_localApiIsLoading && !_localApiIsSaving;

    private bool HasTelemetryChanges => _telemetryBaseline is not null && _telemetryHasChanges;
    private bool CanSaveTelemetry => _telemetryEditContext is not null && !_telemetryIsLoading && !_telemetryIsSaving && HasTelemetryChanges;
    private bool CanCancelTelemetry => !_telemetryIsLoading && !_telemetryIsSaving && HasTelemetryChanges;
    private bool CanReloadTelemetry => !_telemetryIsLoading && !_telemetryIsSaving;

    private bool IsBusy => _observatoryIsLoading || _observatoryIsSaving || _localApiIsLoading || _localApiIsSaving || _telemetryIsLoading || _telemetryIsSaving;
    private bool CanReloadAll => !_observatoryIsLoading && !_localApiIsLoading && !_telemetryIsLoading && !_observatoryIsSaving && !_localApiIsSaving && !_telemetryIsSaving;

    protected override async Task OnInitializedAsync()
    {
        _timeZones = TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(zone => zone.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await ReloadAllAsync().ConfigureAwait(false);
    }

    private async Task ReloadAllAsync()
    {
        if (!CanReloadAll)
        {
            return;
        }

        await ReloadObservatoryAsync().ConfigureAwait(false);
        await ReloadLocalApiAsync().ConfigureAwait(false);
        await ReloadTelemetryAsync().ConfigureAwait(false);
    }

    private void ToggleObservatorySection()
    {
        _observatoryCollapsed = !_observatoryCollapsed;
        StateHasChanged();
    }

    private void ToggleLocalApiSection()
    {
        _localApiCollapsed = !_localApiCollapsed;
        StateHasChanged();
    }

    private void ToggleTelemetrySection()
    {
        _telemetryCollapsed = !_telemetryCollapsed;
        StateHasChanged();
    }

    private static string GetCollapseIconCss(bool collapsed) => collapsed ? "bi bi-chevron-down" : "bi bi-chevron-up";

    private static string GetCollapseCss(bool collapsed) => collapsed ? "collapse-hidden" : string.Empty;

    private static string GetCollapseButtonTitle(string sectionName, bool collapsed) => collapsed ? $"Expand {sectionName}" : $"Collapse {sectionName}";

    private string GetToolbarStatus()
    {
        if (!string.IsNullOrWhiteSpace(_observatoryError) ||
            !string.IsNullOrWhiteSpace(_localApiError) ||
            !string.IsNullOrWhiteSpace(_telemetryError))
        {
            return "Attention required.";
        }

        if ((_observatoryEditContext is null && !_observatoryIsLoading) ||
            (_localApiEditContext is null && !_localApiIsLoading) ||
            (_telemetryEditContext is null && !_telemetryIsLoading))
        {
            return "Awaiting configuration data.";
        }

        if (HasObservatoryChanges || HasLocalApiChanges || HasTelemetryChanges)
        {
            return "Unsaved changes pending.";
        }

        return "Ready.";
    }

    private async Task ReloadObservatoryAsync()
    {
        _observatoryIsLoading = true;
        _observatoryError = null;
        _observatorySuccessMessage = null;
        _observatoryHasChanges = false;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var result = (await LocalApiClient.GetSystemObservatoryAsync(CancellationToken).ConfigureAwait(false))
                .ToResult("Observatory configuration is unavailable from the local API.");

            if (result.IsFailure)
            {
                _observatorySnapshot = null;
                _observatoryBaseline = null;
                _observatoryHasChanges = false;
                DetachObservatoryEditContext();
                _observatoryError = result.Error?.Message ?? "Observatory configuration is unavailable from the local API.";
                return;
            }

            ApplyObservatoryResponse(result.Value);
        }
        catch (OperationCanceledException)
        {
            // Component is disposing; ignore.
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load observatory configuration.");
            _observatorySnapshot = null;
            _observatoryBaseline = null;
            _observatoryHasChanges = false;
            DetachObservatoryEditContext();
            _observatoryError = ex.Message;
        }
        finally
        {
            _observatoryIsLoading = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private async Task ReloadLocalApiAsync()
    {
        _localApiIsLoading = true;
        _localApiError = null;
        _localApiSuccessMessage = null;
        _localApiHasChanges = false;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var result = (await LocalApiClient.GetSystemLocalApiAsync(CancellationToken).ConfigureAwait(false))
                .ToResult("Local API client settings are unavailable from the local API.");

            if (result.IsFailure)
            {
                _localApiSnapshot = null;
                _localApiBaseline = null;
                _localApiHasChanges = false;
                DetachLocalApiEditContext();
                _localApiError = result.Error?.Message ?? "Local API client settings are unavailable from the local API.";
                return;
            }

            ApplyLocalApiResponse(result.Value);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load local API client configuration.");
            _localApiSnapshot = null;
            _localApiBaseline = null;
            _localApiHasChanges = false;
            DetachLocalApiEditContext();
            _localApiError = ex.Message;
        }
        finally
        {
            _localApiIsLoading = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private async Task ReloadTelemetryAsync()
    {
        _telemetryIsLoading = true;
        _telemetryError = null;
        _telemetrySuccessMessage = null;
        _telemetryHasChanges = false;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var result = (await LocalApiClient.GetTelemetryRetentionAsync(CancellationToken).ConfigureAwait(false))
                .ToResult("Telemetry retention settings are unavailable from the local API.");

            if (result.IsFailure)
            {
                _telemetrySnapshot = null;
                _telemetryBaseline = null;
                _telemetryHasChanges = false;
                DetachTelemetryEditContext();
                _telemetryError = result.Error?.Message ?? "Telemetry retention settings are unavailable from the local API.";
                return;
            }

            ApplyTelemetryResponse(result.Value);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load telemetry retention configuration.");
            _telemetrySnapshot = null;
            _telemetryBaseline = null;
            _telemetryHasChanges = false;
            DetachTelemetryEditContext();
            _telemetryError = ex.Message;
        }
        finally
        {
            _telemetryIsLoading = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private async Task SubmitObservatoryAsync()
    {
        if (_observatoryEditContext is null || _observatoryIsLoading || _observatoryIsSaving)
        {
            return;
        }

        _observatorySuccessMessage = null;

        var isValid = _observatoryEditContext.Validate();
        if (!isValid)
        {
            _observatoryError = "Please resolve validation errors before saving.";
            await RequestRepaintAsync().ConfigureAwait(false);
            return;
        }

        await PersistObservatoryAsync().ConfigureAwait(false);
    }

    private async Task PersistObservatoryAsync()
    {
        if (_observatoryIsSaving || !HasObservatoryChanges)
        {
            return;
        }

        _observatoryIsSaving = true;
        _observatoryError = null;
        _observatorySuccessMessage = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var payload = Clone(_observatoryEdit);
            var result = (await LocalApiClient.UpdateSystemObservatoryAsync(payload, CancellationToken).ConfigureAwait(false))
                .ToResult("The local API responded without data while saving observatory settings.");

            if (result.IsFailure)
            {
                _observatoryError = result.Error?.Message ?? "The local API responded without data while saving observatory settings.";
                return;
            }

            ApplyObservatoryResponse(result.Value);
            _observatorySuccessMessage = $"Saved {FormatTimestamp(TimeProvider.GetUtcNow())} (rev {_observatoryEdit.Revision}).";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to save observatory configuration.");
            _observatoryError = ex.Message;
        }
        finally
        {
            _observatoryIsSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private Task CancelObservatoryAsync()
    {
        if (!CanCancelObservatory || _observatoryBaseline is null)
        {
            return Task.CompletedTask;
        }

        _observatoryError = null;
        _observatorySuccessMessage = null;
        _observatoryEdit = Clone(_observatoryBaseline);
        AttachObservatoryEditContext();
        UpdateObservatoryChangeState();
        return RequestRepaintAsync();
    }

    private async Task SubmitLocalApiAsync()
    {
        if (_localApiEditContext is null || _localApiIsLoading || _localApiIsSaving)
        {
            return;
        }

        _localApiSuccessMessage = null;

        var isValid = _localApiEditContext.Validate();
        if (!isValid)
        {
            _localApiError = "Please resolve validation errors before saving.";
            await RequestRepaintAsync().ConfigureAwait(false);
            return;
        }

        await PersistLocalApiAsync().ConfigureAwait(false);
    }

    private async Task PersistLocalApiAsync()
    {
        if (_localApiIsSaving || !HasLocalApiChanges)
        {
            return;
        }

        _localApiIsSaving = true;
        _localApiError = null;
        _localApiSuccessMessage = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var payload = Clone(_localApiEdit);
            var result = (await LocalApiClient.UpdateSystemLocalApiAsync(payload, CancellationToken).ConfigureAwait(false))
                .ToResult("The local API responded without data while saving client settings.");

            if (result.IsFailure)
            {
                _localApiError = result.Error?.Message ?? "The local API responded without data while saving client settings.";
                return;
            }

            ApplyLocalApiResponse(result.Value);
            _localApiSuccessMessage = $"Saved {FormatTimestamp(TimeProvider.GetUtcNow())} (rev {_localApiEdit.Revision}).";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to save local API client configuration.");
            _localApiError = ex.Message;
        }
        finally
        {
            _localApiIsSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private Task CancelLocalApiAsync()
    {
        if (!CanCancelLocalApi || _localApiBaseline is null)
        {
            return Task.CompletedTask;
        }

        _localApiError = null;
        _localApiSuccessMessage = null;
        _localApiEdit = Clone(_localApiBaseline);
        AttachLocalApiEditContext();
        UpdateLocalApiChangeState();
        return RequestRepaintAsync();
    }

    private async Task SubmitTelemetryAsync()
    {
        if (_telemetryEditContext is null || _telemetryIsLoading || _telemetryIsSaving)
        {
            return;
        }

        _telemetrySuccessMessage = null;

        var isValid = _telemetryEditContext.Validate();
        if (!isValid)
        {
            _telemetryError = "Please resolve validation errors before saving.";
            await RequestRepaintAsync().ConfigureAwait(false);
            return;
        }

        await PersistTelemetryAsync().ConfigureAwait(false);
    }

    private async Task PersistTelemetryAsync()
    {
        if (_telemetryIsSaving || !HasTelemetryChanges)
        {
            return;
        }

        _telemetryIsSaving = true;
        _telemetryError = null;
        _telemetrySuccessMessage = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var payload = Clone(_telemetryEdit);
            var result = (await LocalApiClient.UpdateTelemetryRetentionAsync(payload, CancellationToken).ConfigureAwait(false))
                .ToResult("The local API responded without data while saving telemetry retention settings.");

            if (result.IsFailure)
            {
                _telemetryError = result.Error?.Message ?? "The local API responded without data while saving telemetry retention settings.";
                return;
            }

            ApplyTelemetryResponse(result.Value);
            _telemetrySuccessMessage = $"Saved {FormatTimestamp(TimeProvider.GetUtcNow())} (rev {_telemetryEdit.Revision}).";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to save telemetry retention configuration.");
            _telemetryError = ex.Message;
        }
        finally
        {
            _telemetryIsSaving = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private Task CancelTelemetryAsync()
    {
        if (!CanCancelTelemetry || _telemetryBaseline is null)
        {
            return Task.CompletedTask;
        }

        _telemetryError = null;
        _telemetrySuccessMessage = null;
        _telemetryEdit = Clone(_telemetryBaseline);
        AttachTelemetryEditContext();
        UpdateTelemetryChangeState();
        return RequestRepaintAsync();
    }

    private void ApplyObservatoryResponse(SystemObservatoryConfigurationResponse response)
    {
        _observatorySnapshot = response;
        var request = Map(response);
        _observatoryBaseline = Clone(request);
        _observatoryEdit = Clone(request);
        AttachObservatoryEditContext();
        UpdateObservatoryChangeState();
        _observatoryError = null;
    }

    private void ApplyLocalApiResponse(SystemLocalApiConfigurationResponse response)
    {
        _localApiSnapshot = response;
        var request = Map(response);
        _localApiBaseline = Clone(request);
        _localApiEdit = Clone(request);
        AttachLocalApiEditContext();
        UpdateLocalApiChangeState();
        _localApiError = null;
    }

    private void ApplyTelemetryResponse(SystemTelemetryRetentionConfigurationResponse response)
    {
        _telemetrySnapshot = response;
        var request = Map(response);
        _telemetryBaseline = Clone(request);
        _telemetryEdit = Clone(request);
        AttachTelemetryEditContext();
        UpdateTelemetryChangeState();
        _telemetryError = null;
    }

    private void AttachObservatoryEditContext()
    {
        DetachObservatoryEditContext();
        _observatoryEditContext = new EditContext(_observatoryEdit);
        _observatoryEditContext.OnFieldChanged += OnObservatoryFieldChanged;
        UpdateObservatoryChangeState();
    }

    private void AttachLocalApiEditContext()
    {
        DetachLocalApiEditContext();
        _localApiEditContext = new EditContext(_localApiEdit);
        _localApiEditContext.OnFieldChanged += OnLocalApiFieldChanged;
        UpdateLocalApiChangeState();
    }

    private void AttachTelemetryEditContext()
    {
        DetachTelemetryEditContext();
        _telemetryEditContext = new EditContext(_telemetryEdit);
        _telemetryEditContext.OnFieldChanged += OnTelemetryFieldChanged;
        UpdateTelemetryChangeState();
    }

    private void DetachObservatoryEditContext()
    {
        if (_observatoryEditContext is not null)
        {
            _observatoryEditContext.OnFieldChanged -= OnObservatoryFieldChanged;
            _observatoryEditContext = null;
        }
    }

    private void DetachLocalApiEditContext()
    {
        if (_localApiEditContext is not null)
        {
            _localApiEditContext.OnFieldChanged -= OnLocalApiFieldChanged;
            _localApiEditContext = null;
        }
    }

    private void DetachTelemetryEditContext()
    {
        if (_telemetryEditContext is not null)
        {
            _telemetryEditContext.OnFieldChanged -= OnTelemetryFieldChanged;
            _telemetryEditContext = null;
        }
    }

    private void OnObservatoryFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _observatorySuccessMessage = null;
        _observatoryError = null;
        UpdateObservatoryChangeState();
        _ = _observatoryEditContext?.Validate();
        _ = RequestRepaintAsync();
    }

    private void OnLocalApiFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _localApiSuccessMessage = null;
        _localApiError = null;
        UpdateLocalApiChangeState();
        _ = _localApiEditContext?.Validate();
        _ = RequestRepaintAsync();
    }

    private void OnTelemetryFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _telemetrySuccessMessage = null;
        _telemetryError = null;
        UpdateTelemetryChangeState();
        _ = _telemetryEditContext?.Validate();
        _ = RequestRepaintAsync();
    }

    private void UpdateObservatoryChangeState()
    {
        _observatoryHasChanges = _observatoryBaseline is not null && !ObservatoryEquals(_observatoryEdit, _observatoryBaseline);
    }

    private void UpdateLocalApiChangeState()
    {
        _localApiHasChanges = _localApiBaseline is not null && !LocalApiEquals(_localApiEdit, _localApiBaseline);
    }

    private void UpdateTelemetryChangeState()
    {
        _telemetryHasChanges = _telemetryBaseline is not null && !TelemetryEquals(_telemetryEdit, _telemetryBaseline);
    }

    private async Task RequestRepaintAsync()
    {
        if (!CancellationToken.IsCancellationRequested)
        {
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
    }

    private static UpdateSystemObservatoryRequest Map(SystemObservatoryConfigurationResponse response)
        => new()
        {
            Id = response.Id,
            Revision = response.Revision,
            Slug = response.Slug,
            Name = response.Name,
            LatitudeDegrees = response.LatitudeDegrees,
            LongitudeDegrees = response.LongitudeDegrees,
            TimeZoneId = response.TimeZoneId
        };

    private static UpdateSystemLocalApiRequest Map(SystemLocalApiConfigurationResponse response)
        => new()
        {
            Revision = response.Revision,
            BaseAddress = response.BaseAddress ?? string.Empty,
            ApiKey = response.ApiKey ?? string.Empty,
            ApiKeyHeaderName = response.ApiKeyHeaderName,
            TimeoutSeconds = Math.Clamp(response.Timeout.TotalSeconds, 0.1d, 600d)
        };

    private static UpdateSystemTelemetryRetentionRequest Map(SystemTelemetryRetentionConfigurationResponse response)
        => new()
        {
            Revision = response.Revision,
            SweepIntervalSeconds = Math.Clamp(response.SweepInterval.TotalSeconds, 0.1d, 86400d),
            VacuumAfterPurge = response.VacuumAfterPurge,
            RemoteDispatch = Clone(response.RemoteDispatch),
            FrameExports = Clone(response.FrameExports),
            BackgroundStacker = Clone(response.BackgroundStacker),
            CapturePacing = Clone(response.CapturePacing),
            ProcessingQueue = Clone(response.ProcessingQueue),
            FilterMetrics = Clone(response.FilterMetrics),
            TelemetryEvents = Clone(response.TelemetryEvents)
        };

    private static UpdateSystemObservatoryRequest Clone(UpdateSystemObservatoryRequest source)
        => new()
        {
            Id = source.Id,
            Revision = source.Revision,
            Slug = source.Slug,
            Name = source.Name,
            LatitudeDegrees = source.LatitudeDegrees,
            LongitudeDegrees = source.LongitudeDegrees,
            TimeZoneId = source.TimeZoneId
        };

    private static UpdateSystemLocalApiRequest Clone(UpdateSystemLocalApiRequest source)
        => new()
        {
            Revision = source.Revision,
            BaseAddress = source.BaseAddress,
            ApiKey = source.ApiKey,
            ApiKeyHeaderName = source.ApiKeyHeaderName,
            TimeoutSeconds = source.TimeoutSeconds
        };

    private static UpdateSystemTelemetryRetentionRequest Clone(UpdateSystemTelemetryRetentionRequest source)
        => new()
        {
            Revision = source.Revision,
            SweepIntervalSeconds = source.SweepIntervalSeconds,
            VacuumAfterPurge = source.VacuumAfterPurge,
            RemoteDispatch = Clone(source.RemoteDispatch),
            FrameExports = Clone(source.FrameExports),
            BackgroundStacker = Clone(source.BackgroundStacker),
            CapturePacing = Clone(source.CapturePacing),
            ProcessingQueue = Clone(source.ProcessingQueue),
            FilterMetrics = Clone(source.FilterMetrics),
            TelemetryEvents = Clone(source.TelemetryEvents)
        };

    private static TelemetryRetentionPolicyModel Clone(TelemetryRetentionPolicyModel? source)
        => new()
        {
            MaxAgeSeconds = source?.MaxAgeSeconds,
            MaxRecords = source?.MaxRecords
        };

    private static bool ObservatoryEquals(UpdateSystemObservatoryRequest current, UpdateSystemObservatoryRequest baseline)
    {
        return current.Id == baseline.Id
            && current.Revision == baseline.Revision
            && string.Equals(Normalize(current.Slug), Normalize(baseline.Slug), StringComparison.Ordinal)
            && string.Equals(Normalize(current.Name), Normalize(baseline.Name), StringComparison.Ordinal)
            && Math.Abs(current.LatitudeDegrees - baseline.LatitudeDegrees) < 0.000001
            && Math.Abs(current.LongitudeDegrees - baseline.LongitudeDegrees) < 0.000001
            && string.Equals(Normalize(current.TimeZoneId), Normalize(baseline.TimeZoneId), StringComparison.Ordinal);
    }

    private static bool LocalApiEquals(UpdateSystemLocalApiRequest current, UpdateSystemLocalApiRequest baseline)
    {
        return current.Revision == baseline.Revision
            && string.Equals(Normalize(current.BaseAddress), Normalize(baseline.BaseAddress), StringComparison.Ordinal)
            && string.Equals(Normalize(current.ApiKey), Normalize(baseline.ApiKey), StringComparison.Ordinal)
            && string.Equals(Normalize(current.ApiKeyHeaderName), Normalize(baseline.ApiKeyHeaderName), StringComparison.Ordinal)
            && Math.Abs(current.TimeoutSeconds - baseline.TimeoutSeconds) < 0.0001;
    }

    private static bool TelemetryEquals(UpdateSystemTelemetryRetentionRequest current, UpdateSystemTelemetryRetentionRequest baseline)
    {
        return current.Revision == baseline.Revision
            && Math.Abs(current.SweepIntervalSeconds - baseline.SweepIntervalSeconds) < 0.0001
            && current.VacuumAfterPurge == baseline.VacuumAfterPurge
            && PolicyEquals(current.RemoteDispatch, baseline.RemoteDispatch)
            && PolicyEquals(current.FrameExports, baseline.FrameExports)
            && PolicyEquals(current.BackgroundStacker, baseline.BackgroundStacker)
            && PolicyEquals(current.CapturePacing, baseline.CapturePacing)
            && PolicyEquals(current.ProcessingQueue, baseline.ProcessingQueue)
            && PolicyEquals(current.FilterMetrics, baseline.FilterMetrics)
            && PolicyEquals(current.TelemetryEvents, baseline.TelemetryEvents);
    }

    private static bool PolicyEquals(TelemetryRetentionPolicyModel? current, TelemetryRetentionPolicyModel? baseline)
    {
        var left = current ?? new TelemetryRetentionPolicyModel();
        var right = baseline ?? new TelemetryRetentionPolicyModel();

        var ageEqual = left.MaxAgeSeconds.HasValue == right.MaxAgeSeconds.HasValue
            && (!left.MaxAgeSeconds.HasValue || Math.Abs(left.MaxAgeSeconds.Value - right.MaxAgeSeconds!.Value) < 0.0001);

        var recordsEqual = left.MaxRecords == right.MaxRecords;
        return ageEqual && recordsEqual;
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private string FormatTimestamp(DateTimeOffset timestamp) => timestamp.ToLocalTime().ToString("HH:mm:ss");

    public void Dispose()
    {
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        DetachObservatoryEditContext();
        DetachLocalApiEditContext();
        DetachTelemetryEditContext();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
