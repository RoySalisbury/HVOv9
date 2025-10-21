using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;
using HVO.SkyMonitorV5.RPi.Models;
using HVO.SkyMonitorV5.RPi.Models.System;
using HVO.SkyMonitorV5.RPi.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace HVO.SkyMonitorV5.RPi.Components.Configuration;

public sealed partial class AdapterDiagnosticsTab : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    private AllSkyStatusResponse? _status;
    private RigRuntimeStatusResponse? _runtime;
    private bool _isLoading;
    private bool _runtimeCollapsed;
    private bool _rigCollapsed;
    private string? _errorMessage;
    private string? _runtimeError;
    private string? _actionMessage;
    private string? _actionError;
    private bool _actionInFlight;
    private DateTimeOffset? _lastRefreshed;

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    [Inject]
    public ILogger<AdapterDiagnosticsTab>? Logger { get; set; }

    private CancellationToken CancellationToken => _lifetime.Token;

    private bool CanRefresh => !_isLoading && !_actionInFlight;

    private bool CanForceReload => _runtime?.Capabilities.CanForceReload == true && !_actionInFlight;

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            _lifetime.Cancel();
        }

        _lifetime.Dispose();
    }

    private async Task RefreshAsync()
    {
        if (!CanRefresh)
        {
            return;
        }

        _isLoading = true;
        _errorMessage = null;
        _runtimeError = null;
        _actionMessage = null;
        _actionError = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            await LoadRuntimeStatusAsync().ConfigureAwait(false);
            await LoadAllSkyStatusAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isLoading = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private async Task LoadRuntimeStatusAsync()
    {
        try
        {
            var runtime = await LocalApiClient.GetRigRuntimeStatusAsync(CancellationToken).ConfigureAwait(false);
            if (runtime is null)
            {
                _runtime = null;
                _runtimeError = "Unable to retrieve rig runtime status from the local API.";
            }
            else
            {
                _runtime = runtime;
                _runtimeError = null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load rig runtime status.");
            _runtime = null;
            _runtimeError = ex.Message;
        }
    }

    private async Task LoadAllSkyStatusAsync()
    {
        try
        {
            var response = await LocalApiClient.GetAllSkyStatusAsync(CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _errorMessage = "Unable to retrieve adapter diagnostics from the local API.";
                _status = null;
            }
            else
            {
                _status = response;
                _errorMessage = null;
                _lastRefreshed = TimeProvider.GetUtcNow().ToLocalTime();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to refresh adapter diagnostics.");
            _errorMessage = ex.Message;
            _status = null;
        }
    }

    private async Task ExecuteActionAsync(RigRuntimeActionKind action, bool forceRestart = false)
    {
        if (_actionInFlight)
        {
            return;
        }

        if (action == RigRuntimeActionKind.Reload)
        {
            if (_runtime is null || !_runtime.Capabilities.CanReload || (forceRestart && !_runtime.Capabilities.CanForceReload))
            {
                return;
            }
        }
        else if (!CanExecuteAction(action))
        {
            return;
        }

        _actionInFlight = true;
        _actionMessage = null;
        _actionError = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            var request = new RigRuntimeActionRequest
            {
                Action = action,
                ForceRestart = forceRestart
            };

            var response = await LocalApiClient.ExecuteRigRuntimeActionAsync(request, CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _actionError = "Adapter action failed; local API returned no response.";
                await LoadRuntimeStatusAsync().ConfigureAwait(false);
                return;
            }

            _runtime = response.Status;
            _runtimeError = null;

            if (response.Succeeded)
            {
                _actionMessage = response.Message;
            }
            else
            {
                _actionError = response.Message;
            }

            await LoadAllSkyStatusAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Adapter lifecycle action {Action} failed.", action);
            _actionError = ex.Message;
            await LoadRuntimeStatusAsync().ConfigureAwait(false);
        }
        finally
        {
            _actionInFlight = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    private bool CanExecuteAction(RigRuntimeActionKind action)
    {
        if (_runtime is null || _actionInFlight)
        {
            return false;
        }

        return action switch
        {
            RigRuntimeActionKind.Start => _runtime.Capabilities.CanStart,
            RigRuntimeActionKind.Pause => _runtime.Capabilities.CanPause,
            RigRuntimeActionKind.Resume => _runtime.Capabilities.CanResume,
            RigRuntimeActionKind.Stop => _runtime.Capabilities.CanStop,
            RigRuntimeActionKind.Reload => _runtime.Capabilities.CanReload,
            _ => false
        };
    }

    private void ToggleRuntimeSection()
    {
        _runtimeCollapsed = !_runtimeCollapsed;
    }

    private void ToggleRigSection()
    {
        _rigCollapsed = !_rigCollapsed;
    }

    private static string GetCollapseIconCss(bool collapsed)
        => collapsed ? "bi bi-chevron-down" : "bi bi-chevron-up";

    private static string GetCollapseCss(bool collapsed)
        => collapsed ? "collapse-hidden" : string.Empty;

    private static string GetCollapseButtonTitle(string sectionName, bool collapsed)
        => collapsed ? $"Expand {sectionName}" : $"Collapse {sectionName}";

    private string GetToolbarStatus()
    {
        if (!string.IsNullOrWhiteSpace(_runtimeError))
        {
            return _runtimeError!;
        }

        if (!string.IsNullOrWhiteSpace(_errorMessage))
        {
            return "Adapter diagnostics failed to load.";
        }

        if (_runtime is null && _status is null)
        {
            return _isLoading ? "Loading adapter diagnostics…" : "No adapter diagnostics available.";
        }

        var stateText = _runtime is null ? GetCaptureState() : _runtime.State.ToString();

        string frameText;
        if (_status?.LastFrameTimestamp is null)
        {
            frameText = _status is null ? "no telemetry" : "no frames yet";
        }
        else
        {
            frameText = FormattableString.Invariant($"last frame {FormatRelativeTimestamp(_status.LastFrameTimestamp)}");
        }

        var refreshedText = _lastRefreshed is null
            ? string.Empty
            : FormattableString.Invariant($", refreshed {FormatRelativeTimestamp(_lastRefreshed)}");

        var runtimeMessage = _runtime is null || string.IsNullOrWhiteSpace(_runtime.Message)
            ? string.Empty
            : FormattableString.Invariant($", {_runtime.Message}");

        return FormattableString.Invariant($"{stateText} · {frameText}{refreshedText}{runtimeMessage}");
    }

    private string GetToolbarStatusCss()
    {
        if (!string.IsNullOrWhiteSpace(_runtimeError) || !string.IsNullOrWhiteSpace(_actionError))
        {
            return "text-danger";
        }

        if (!string.IsNullOrWhiteSpace(_errorMessage))
        {
            return "text-danger";
        }

        if (_isLoading || _actionInFlight)
        {
            return "text-muted";
        }

        var runtimeState = _runtime?.State;
        if (runtimeState == RigAdapterLifecycleState.Paused)
        {
            return "text-warning";
        }

        return string.Equals(GetCaptureState(), "Error", StringComparison.OrdinalIgnoreCase)
            ? "text-warning"
            : "text-muted";
    }

    private string GetCaptureState()
    {
        if (_status is null)
        {
            return _runtime?.State.ToString() ?? "Unknown";
        }

        var summaryState = _status.Summary?.Camera?.Status;
        if (!string.IsNullOrWhiteSpace(summaryState))
        {
            return summaryState;
        }

        return _status.IsRunning ? "Capturing" : "Idle";
    }

    private string GetRuntimeSubtitle()
    {
        if (_runtime is null)
        {
            return _runtimeError ?? "Runtime status unavailable.";
        }

        var timestamp = _runtime.TimestampUtc.ToLocalTime();
        var relative = FormatRelativeTimestamp(timestamp);
        var message = string.IsNullOrWhiteSpace(_runtime.Message)
            ? string.Empty
            : _runtime.Message.Trim();

        return string.IsNullOrWhiteSpace(message)
            ? FormattableString.Invariant($"Updated {relative}.")
            : FormattableString.Invariant($"{message} · updated {relative}");
    }

    private string GetRuntimeStateBadgeCss()
        => _runtime?.State switch
        {
            RigAdapterLifecycleState.Running => "badge bg-success-subtle text-success-emphasis",
            RigAdapterLifecycleState.Paused => "badge bg-warning-subtle text-warning-emphasis",
            RigAdapterLifecycleState.Stopped => "badge bg-secondary-subtle text-secondary-emphasis",
            _ => "badge bg-secondary"
        };

    private string GetAdapterStateSummary()
    {
        if (_runtime is null)
        {
            return "Adapter state unavailable.";
        }

        var timestamp = _runtime.TimestampUtc.ToLocalTime();
        var relative = FormatRelativeTimestamp(timestamp);
        var message = string.IsNullOrWhiteSpace(_runtime.Message) ? string.Empty : _runtime.Message.Trim();
        var detail = string.IsNullOrWhiteSpace(message)
            ? relative
            : FormattableString.Invariant($"{message} · {relative}");

        return FormattableString.Invariant($"{_runtime.State} · {detail}");
    }

    private string GetLastFrameSummary()
    {
        if (_status?.LastFrameTimestamp is null)
        {
            return "No frames received.";
        }

        var raw = _status.RawFrame;
        if (raw is null)
        {
            return FormattableString.Invariant($"{FormatTimestamp(_status.LastFrameTimestamp)}");
        }

        return FormattableString.Invariant($"{FormatTimestamp(raw.Timestamp)} · {raw.Width}×{raw.Height}px · {raw.ExposureMilliseconds} ms @ gain {raw.Gain}");
    }

    private string GetExposureSummary()
    {
        var exposure = _status?.Summary?.Camera;
        if (exposure is null)
        {
            return "Exposure data unavailable.";
        }

        return FormattableString.Invariant($"{exposure.ExposureMilliseconds} ms · gain {exposure.Gain}");
    }

    private string GetBackgroundStackerSummary()
    {
        var status = _status?.Summary?.BackgroundStacker;
        if (status is null)
        {
            return "Background stacker telemetry unavailable.";
        }

        var enabled = status.Enabled ? "Enabled" : "Disabled";
        var queue = FormattableString.Invariant($"queue {status.QueueDepth}/{status.QueueCapacity}");
        var lastCompleted = status.LastCompletedAt is null
            ? "no completions yet"
            : FormattableString.Invariant($"last completed {FormatRelativeTimestamp(status.LastCompletedAt)}");

        return FormattableString.Invariant($"{enabled} · {queue} · {lastCompleted}");
    }

    private string GetCapturePacingSummary()
    {
        var pacing = _status?.Summary?.CapturePacing;
        if (pacing is null)
        {
            return "Capture pacing telemetry unavailable.";
        }

        var baseDelay = pacing.BaseDelayMilliseconds;
        var adjusted = pacing.AdjustedDelayMilliseconds;
        var penalty = pacing.PenaltyActive
            ? FormattableString.Invariant($"penalty active until {FormatTimestamp(pacing.PenaltyExpiresAt)}")
            : "no penalty";

        return FormattableString.Invariant($"delay {adjusted} ms (base {baseDelay} ms) · {penalty}");
    }

    private string GetProcessingQueueSummary()
    {
        var queue = _status?.Summary?.ProcessingQueue;
        if (queue is null)
        {
            return "Processing queue telemetry unavailable.";
        }

        var enabled = queue.Enabled ? "Enabled" : "Disabled";
        return FormattableString.Invariant($"{enabled} · depth {queue.Depth}/{queue.Capacity} · backpressure {queue.BackpressureEvents} · last update {FormatRelativeTimestamp(queue.Timestamp)}");
    }

    private string GetRemoteDispatchSummary()
    {
        var dispatch = _status?.Summary?.RemoteDispatch;
        if (dispatch is null)
        {
            return "Remote dispatch telemetry unavailable.";
        }

        var outcome = dispatch.Outcome.ToString();
        var detail = string.IsNullOrWhiteSpace(dispatch.Message)
            ? string.IsNullOrWhiteSpace(dispatch.ErrorMessage) ? "no recent messages" : dispatch.ErrorMessage
            : dispatch.Message;

        return FormattableString.Invariant($"{outcome} · {detail} · last {FormatRelativeTimestamp(dispatch.Timestamp)}");
    }

    private string GetAdapterName()
        => _runtime?.AdapterName ?? _status?.Camera?.AdapterName ?? "Unknown";

    private string GetDriverVersion()
    {
        if (!string.IsNullOrWhiteSpace(_runtime?.DriverIdentifier))
        {
            return _runtime.DriverIdentifier;
        }

        return _status?.Camera?.DriverVersion ?? "Unknown";
    }

    private IReadOnlyList<string> GetPipelineCapabilities()
        => _status?.Summary?.Camera?.Capabilities ?? Array.Empty<string>();

    private string GetRigName()
    {
        if (!string.IsNullOrWhiteSpace(_runtime?.RigName))
        {
            return _runtime.RigName;
        }

        if (_status?.Rig is null)
        {
            return "Unknown";
        }

        return string.IsNullOrWhiteSpace(_status.Rig.Name) ? "Unnamed rig" : _status.Rig.Name;
    }

    private string GetSensorSummary()
    {
        var sensor = _status?.Rig?.Sensor;
        if (sensor is null)
        {
            return "Sensor data unavailable.";
        }

        return FormattableString.Invariant($"{sensor.WidthPx}×{sensor.HeightPx}px · {sensor.PixelSizeMicrons:F2} µm pixels");
    }

    private string GetLensSummary()
    {
        var lens = _status?.Rig?.Lens;
        if (lens is null)
        {
            return "Lens data unavailable.";
        }

        return FormattableString.Invariant($"{lens.Name} ({lens.Kind}) · {lens.FocalLengthMm:F1} mm · FoV {lens.FovXDeg:F1}°");
    }

    private IReadOnlyList<string> GetHardwareCapabilities()
        => _status?.Summary?.Camera?.HardwareCapabilities ?? Array.Empty<string>();

    private static string FormatTimestamp(DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return "—";
        }

        return timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private string FormatRelativeTimestamp(DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return "—";
        }

        var local = timestamp.Value.ToLocalTime();
        var now = TimeProvider.GetLocalNow();
        var diff = now - local;

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
