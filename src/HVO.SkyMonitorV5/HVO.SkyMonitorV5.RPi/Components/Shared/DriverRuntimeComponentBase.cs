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

namespace HVO.SkyMonitorV5.RPi.Components.Shared;

/// <summary>
/// Shared lifecycle and telemetry coordination for components interacting with the SkyMonitor driver runtime.
/// </summary>
public abstract class DriverRuntimeComponentBase<TComponent> : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    private AllSkyStatusResponse? _status;
    private RigRuntimeStatusResponse? _runtime;
    private bool _isLoading;
    private string? _captureError;
    private string? _runtimeError;
    private string? _actionMessage;
    private string? _actionError;
    private bool _actionInFlight;

    [Inject]
    public ILocalApiClient LocalApiClient { get; set; } = default!;

    [Inject]
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    [Inject]
    public ILogger<TComponent>? Logger { get; set; }

    protected AllSkyStatusResponse? Status => _status;

    protected RigRuntimeStatusResponse? Runtime => _runtime;

    protected bool IsLoading => _isLoading;

    protected string? CaptureError => _captureError;

    protected string? RuntimeError => _runtimeError;

    protected string? ActionMessage => _actionMessage;

    protected string? ActionError => _actionError;

    protected bool ActionInFlight => _actionInFlight;

    protected bool CanRefresh => !_isLoading && !_actionInFlight;

    protected bool CanForceReload => _runtime?.Capabilities.CanForceReload == true && !_actionInFlight;

    protected CancellationToken CancellationToken => _lifetime.Token;

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

    protected async Task RefreshAsync()
    {
        if (!CanRefresh)
        {
            return;
        }

        _isLoading = true;
        _captureError = null;
        _runtimeError = null;
        _actionMessage = null;
        _actionError = null;
        await RequestRepaintAsync().ConfigureAwait(false);

        try
        {
            await LoadRuntimeStatusAsync().ConfigureAwait(false);
            await LoadCaptureStatusAsync().ConfigureAwait(false);
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

    protected async Task ExecuteActionAsync(RigRuntimeActionKind action, bool forceRestart = false)
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
                _actionError = "Driver action failed; local API returned no response.";
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

            await LoadCaptureStatusAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Driver lifecycle action {Action} failed.", action);
            _actionError = ex.Message;
            await LoadRuntimeStatusAsync().ConfigureAwait(false);
        }
        finally
        {
            _actionInFlight = false;
            await RequestRepaintAsync().ConfigureAwait(false);
        }
    }

    protected bool CanExecuteAction(RigRuntimeActionKind action)
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

    private async Task LoadRuntimeStatusAsync()
    {
        try
        {
            var runtime = await LocalApiClient.GetRigRuntimeStatusAsync(CancellationToken).ConfigureAwait(false);
            if (runtime is null)
            {
                _runtime = null;
                _runtimeError = "Unable to retrieve driver runtime status.";
            }
            else
            {
                _runtime = runtime;
                _runtimeError = null;
            }

            await OnRuntimeUpdatedAsync(_runtime, _runtimeError).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load driver runtime status.");
            _runtime = null;
            _runtimeError = ex.Message;
            await OnRuntimeLoadFailedAsync(ex).ConfigureAwait(false);
        }
    }

    private async Task LoadCaptureStatusAsync()
    {
        try
        {
            var response = await LocalApiClient.GetAllSkyStatusAsync(CancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                _captureError = "Unable to retrieve capture telemetry.";
                _status = null;
            }
            else
            {
                _status = response;
                _captureError = null;
            }

            await OnCaptureUpdatedAsync(_status, _captureError).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load capture telemetry snapshot.");
            _captureError = ex.Message;
            _status = null;
            await OnCaptureLoadFailedAsync(ex).ConfigureAwait(false);
        }
    }

    protected virtual Task OnRuntimeUpdatedAsync(RigRuntimeStatusResponse? runtime, string? error) => Task.CompletedTask;

    protected virtual Task OnCaptureUpdatedAsync(AllSkyStatusResponse? status, string? error) => Task.CompletedTask;

    protected virtual Task OnRuntimeLoadFailedAsync(Exception exception) => Task.CompletedTask;

    protected virtual Task OnCaptureLoadFailedAsync(Exception exception) => Task.CompletedTask;

    protected async Task RequestRepaintAsync()
    {
        try
        {
            await InvokeAsync(StateHasChanged).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected static string FormatTimestamp(DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return "—";
        }

        return timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    protected string FormatRelativeTimestamp(DateTimeOffset? timestamp)
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

    protected string GetRuntimeStateBadgeCss()
        => _runtime?.State switch
        {
            RigAdapterLifecycleState.Running => "badge bg-success-subtle text-success-emphasis",
            RigAdapterLifecycleState.Paused => "badge bg-warning-subtle text-warning-emphasis",
            RigAdapterLifecycleState.Stopped => "badge bg-secondary-subtle text-secondary-emphasis",
            _ => "badge bg-secondary"
        };

    protected string GetDriverDisplayName()
        => _runtime?.AdapterName ?? _status?.Camera?.AdapterName ?? "Unknown";

    protected string GetDriverVersion()
    {
        if (!string.IsNullOrWhiteSpace(_runtime?.DriverIdentifier))
        {
            return _runtime.DriverIdentifier;
        }

        return _status?.Camera?.DriverVersion ?? "Unknown";
    }

    protected string GetRigName()
    {
        if (!string.IsNullOrWhiteSpace(_runtime?.RigName))
        {
            return _runtime.RigName;
        }

        return _status?.Rig?.Name ?? "Unknown";
    }

    protected string GetDriverStateSummary()
    {
        if (_runtime is null)
        {
            return "Driver state unavailable.";
        }

        var timestamp = _runtime.TimestampUtc.ToLocalTime();
        var relative = FormatRelativeTimestamp(timestamp);
        var message = string.IsNullOrWhiteSpace(_runtime.Message) ? string.Empty : _runtime.Message.Trim();
        var detail = string.IsNullOrWhiteSpace(message)
            ? relative
            : FormattableString.Invariant($"{message} · {relative}");

        return FormattableString.Invariant($"{_runtime.State} · {detail}");
    }

    protected string GetCapabilitySummary()
    {
        if (_runtime is null)
        {
            return "Capabilities unavailable.";
        }

        var flags = new List<string>(5);
        if (_runtime.Capabilities.CanStart)
        {
            flags.Add("start");
        }

        if (_runtime.Capabilities.CanPause)
        {
            flags.Add("pause");
        }

        if (_runtime.Capabilities.CanResume)
        {
            flags.Add("resume");
        }

        if (_runtime.Capabilities.CanStop)
        {
            flags.Add("stop");
        }

        if (_runtime.Capabilities.CanReload)
        {
            flags.Add(_runtime.Capabilities.CanForceReload ? "reload/force" : "reload");
        }

        return flags.Count > 0
            ? string.Join(" · ", flags)
            : "No lifecycle actions enabled.";
    }

    protected string GetRuntimeSubtitle()
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

    protected string GetCaptureState()
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

    protected string GetLastFrameSummary()
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

    protected string GetExposureSummary()
    {
        var exposure = _status?.Summary?.Camera;
        if (exposure is null)
        {
            return "Exposure data unavailable.";
        }

        return FormattableString.Invariant($"{exposure.ExposureMilliseconds} ms · gain {exposure.Gain}");
    }

    protected string GetBackgroundStackerSummary()
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

    protected string GetCapturePacingSummary()
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

    protected string GetProcessingQueueSummary()
    {
        var queue = _status?.Summary?.ProcessingQueue;
        if (queue is null)
        {
            return "Processing queue telemetry unavailable.";
        }

        var enabled = queue.Enabled ? "Enabled" : "Disabled";
        return FormattableString.Invariant($"{enabled} · depth {queue.Depth}/{queue.Capacity} · backpressure {queue.BackpressureEvents} · last update {FormatRelativeTimestamp(queue.Timestamp)}");
    }

    protected string GetRemoteDispatchSummary()
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

    protected string GetHardwareSummary()
    {
        var summaries = new List<string>(3);

        var sensor = BuildSensorSummary();
        if (!string.IsNullOrWhiteSpace(sensor))
        {
            summaries.Add(sensor);
        }

        var lens = BuildLensSummary();
        if (!string.IsNullOrWhiteSpace(lens))
        {
            summaries.Add(lens);
        }

        var pipeline = BuildCapabilitySummary();
        if (!string.IsNullOrWhiteSpace(pipeline))
        {
            summaries.Add(pipeline);
        }

        return summaries.Count > 0 ? string.Join(" · ", summaries) : "Hardware data unavailable.";
    }

    private string BuildSensorSummary()
    {
        var sensor = _status?.Rig?.Sensor;
        if (sensor is null)
        {
            return string.Empty;
        }

        return FormattableString.Invariant($"Sensor {sensor.WidthPx}×{sensor.HeightPx}px · {sensor.PixelSizeMicrons:F2} µm");
    }

    private string BuildLensSummary()
    {
        var lens = _status?.Rig?.Lens;
        if (lens is null)
        {
            return string.Empty;
        }

        return FormattableString.Invariant($"Lens {lens.Name} {lens.FocalLengthMm:F1} mm ({lens.Kind})");
    }

    private string BuildCapabilitySummary()
    {
        var capabilities = _status?.Summary?.Camera?.Capabilities ?? Array.Empty<string>();
        return capabilities.Count > 0
            ? string.Join(", ", capabilities)
            : string.Empty;
    }
}
