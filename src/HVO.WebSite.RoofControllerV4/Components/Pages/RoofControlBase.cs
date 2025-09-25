using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Options;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using HVO;
using System.Timers;

namespace HVO.WebSite.RoofControllerV4.Components.Pages;

/// <summary>
/// Base class providing all roof control logic, status handling, and UI helpers.
/// The modern UI (RoofControlV2) inherits this to render the experience.
/// </summary>
public class RoofControlBase : ComponentBase, IDisposable
{
    #region Dependency Injection

    [Inject] protected IRoofControllerServiceV4 RoofController { get; set; } = default!;
    [Inject] protected ILogger<RoofControlBase> Logger { get; set; } = default!;
    [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] protected IOptions<RoofControllerOptionsV4> RoofControllerOptions { get; set; } = default!;

    #endregion

    #region Private Fields

    // Removed polling timer; updates are push-based via service events
    protected readonly List<NotificationMessage> _notifications = new();
    protected bool _isDisposed = false;
    protected RoofControllerStopReason _lastNotifiedStopReason = RoofControllerStopReason.None;
    
    #endregion

    #region Public Properties

    public RoofControllerStatus CurrentStatus => RoofController.Status;
    public bool IsInitialized => RoofController.IsInitialized;
    public bool IsMoving => RoofController.IsMoving;
    public bool IsRoofOpen => RoofController.Status == RoofControllerStatus.Open;
    public bool IsServiceDisposed => RoofController.IsServiceDisposed;
    public bool IsServiceAvailable => RoofController.IsInitialized && !RoofController.IsServiceDisposed;

    public bool IsOpenDisabled 
    { 
        get 
        {
            var baseDisabled = !IsServiceAvailable || RoofController.IsMoving || 
                               RoofController.Status == RoofControllerStatus.Opening || 
                               RoofController.Status == RoofControllerStatus.Open ||
                               RoofController.Status == RoofControllerStatus.Error;
            return baseDisabled;
        }
    }

    public bool IsCloseDisabled 
    { 
        get 
        {
            var baseDisabled = !IsServiceAvailable || RoofController.IsMoving || 
                               RoofController.Status == RoofControllerStatus.Closing || 
                               RoofController.Status == RoofControllerStatus.Closed ||
                               RoofController.Status == RoofControllerStatus.Error;
            return baseDisabled;
        }
    }

    public bool IsStopDisabled => !IsServiceAvailable || !RoofController.IsMoving;
    public bool IsClearFaultDisabled => !IsServiceAvailable || RoofController.IsMoving;
    public IReadOnlyList<NotificationMessage> Notifications => _notifications.AsReadOnly();
    public bool IsSafetyWatchdogRunning => RoofController.IsWatchdogActive;
    public double SafetyWatchdogTimeRemaining => RoofController.WatchdogSecondsRemaining ?? 0;
    public double SafetyWatchdogTimeoutSeconds => RoofControllerOptions.Value.SafetyWatchdogTimeout.TotalSeconds;
    public DateTimeOffset? LastTransitionUtc => RoofController.LastTransitionUtc;
    public RoofControllerStopReason LastStopReason => RoofController.LastStopReason;
    public bool WasEmergencyStop => LastStopReason is RoofControllerStopReason.EmergencyStop or RoofControllerStopReason.SafetyWatchdogTimeout;

    public string GetLastStopTypeLabel()
    {
        return LastStopReason switch
        {
            RoofControllerStopReason.EmergencyStop => "Emergency",
            RoofControllerStopReason.SafetyWatchdogTimeout => "Emergency",
            RoofControllerStopReason.None => "",
            _ => "Normal"
        };
    }

    public string GetLastStopTypeBadgeClass()
    {
        if (string.IsNullOrEmpty(GetLastStopTypeLabel())) return "d-none";
        return WasEmergencyStop ? "badge bg-danger text-white" : "badge bg-secondary";
    }

    #endregion

    #region Component Lifecycle

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Logger.LogInformation("RoofControlBase initializing");
            await UpdateStatusAsync();
            RoofController.StatusChanged += OnServiceStatusChanged;

            AddNotification("UI", "Roof control UI loaded", NotificationType.Info);
            _lastNotifiedStopReason = RoofController.LastStopReason;

            if (IsServiceDisposed)
            {
                AddNotification("Service", "Roof controller disposed", NotificationType.Warning);
            }
            else if (!IsInitialized)
            {
                AddNotification("Service", "Roof controller initializing…", NotificationType.Info);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during initialization");
            AddNotification("Error", "Initialization error", NotificationType.Error);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        RoofController.StatusChanged -= OnServiceStatusChanged;
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Event Handling

    private async void OnServiceStatusChanged(object? sender, EventArgs e)
    {
        try
        {
            await UpdateStatusAsync();

            // Emergency notification on change
            if (RoofController.LastStopReason != _lastNotifiedStopReason &&
                (RoofController.LastStopReason == RoofControllerStopReason.EmergencyStop || RoofController.LastStopReason == RoofControllerStopReason.SafetyWatchdogTimeout))
            {
                AddNotification("Safety", "Emergency stop triggered", NotificationType.Error);
                _lastNotifiedStopReason = RoofController.LastStopReason;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in status changed handler");
        }
    }

    private async Task UpdateStatusAsync()
    {
        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (ObjectDisposedException)
        {
            // Component disposed - ignore
        }
    }

    #endregion

    #region UI Helpers

    public string GetStatusBadgeClass() => CurrentStatus switch
    {
        RoofControllerStatus.Open => "bg-success",
        RoofControllerStatus.Closed => "bg-secondary",
        RoofControllerStatus.Opening => "bg-info",
        RoofControllerStatus.Closing => "bg-info",
        RoofControllerStatus.Error => "bg-danger",
        _ => "bg-dark"
    };

    public string GetHealthCheckBadgeClass() => CurrentStatus == RoofControllerStatus.Error ? "bg-danger" : "bg-success";
    public string GetHealthCheckStatus()
    {
        if (IsServiceDisposed || CurrentStatus == RoofControllerStatus.Error)
            return "Error Detected";
        if (IsInitialized)
            return "Healthy";
        return "Checking...";
    }
    public string GetWatchdogBadgeClass() => IsSafetyWatchdogRunning ? "bg-warning text-dark" : "bg-success";

    public string GetOpenButtonClass() => $"btn btn-success btn-lg control-btn{(IsOpenDisabled ? " disabled" : string.Empty)}";
    public string GetStopButtonClass() => $"btn btn-warning btn-lg control-btn{(IsStopDisabled ? " disabled" : string.Empty)}";
    public string GetCloseButtonClass() => $"btn btn-danger btn-lg control-btn{(IsCloseDisabled ? " disabled" : string.Empty)}";

    public string GetWatchdogProgressBarClass()
    {
        var percent = (SafetyWatchdogTimeoutSeconds - SafetyWatchdogTimeRemaining) / SafetyWatchdogTimeoutSeconds * 100;
        if (percent < 50) return "progress-bar bg-success";
        if (percent < 85) return "progress-bar bg-warning text-dark";
        return "progress-bar bg-danger";
    }

    public string GetToastClass(NotificationType type) => type switch
    {
        NotificationType.Error => "text-bg-danger",
        NotificationType.Warning => "text-bg-warning",
        NotificationType.Success => "text-bg-success",
        _ => "text-bg-dark"
    };

    public string GetToastStyle(NotificationType type) => "background-color: rgba(0,0,0,0.85); border: 1px solid rgba(255,255,255,0.15);";
    public string GetToastIcon(NotificationType type) => type switch
    {
        NotificationType.Error => "bi bi-exclamation-triangle-fill",
        NotificationType.Warning => "bi bi-exclamation-circle-fill",
        NotificationType.Success => "bi bi-check-circle-fill",
        NotificationType.Info => "bi bi-info-circle-fill",
        _ => "bi bi-bell-fill"
    };

    public string GetLastTransitionFriendly()
    {
        if (LastTransitionUtc is null) return "—";
        var local = LastTransitionUtc.Value.ToLocalTime();
        return local.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public string GetLastTransitionTooltip()
    {
        if (LastTransitionUtc is null) return "Timestamp of the last status change";
        return $"UTC: {LastTransitionUtc:yyyy-MM-dd HH:mm:ss}Z";
    }

    #endregion

    #region Operations

    public void OpenRoof()
    {
        if (!IsServiceAvailable) return;
        var result = RoofController.Open();
        if (result.IsSuccessful)
        {
            AddNotification("Command", "Opening roof", NotificationType.Info);
        }
        else
        {
            AddNotification("Error", result.Error?.Message ?? "Failed to open", NotificationType.Error);
        }
    }

    public void CloseRoof()
    {
        if (!IsServiceAvailable) return;
        var result = RoofController.Close();
        if (result.IsSuccessful)
        {
            AddNotification("Command", "Closing roof", NotificationType.Info);
        }
        else
        {
            AddNotification("Error", result.Error?.Message ?? "Failed to close", NotificationType.Error);
        }
    }

    public void StopRoof()
    {
        if (!IsServiceAvailable) return;
        var result = RoofController.Stop(RoofControllerStopReason.NormalStop);
        if (result.IsSuccessful)
        {
            AddNotification("Command", "Stop requested", NotificationType.Info);
        }
        else
        {
            AddNotification("Error", result.Error?.Message ?? "Failed to stop", NotificationType.Error);
        }
    }

    public async Task ClearFaultAsync()
    {
        try
        {
            var result = await RoofController.ClearFault();
            if (result.IsSuccessful)
            {
                AddNotification("Command", "Fault cleared", NotificationType.Success);
            }
            else
            {
                AddNotification("Error", result.Error?.Message ?? "Failed to clear fault", NotificationType.Error);
            }
            await UpdateStatusAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error clearing fault");
            AddNotification("Error", "Error clearing fault", NotificationType.Error);
        }
    }

    protected void AddNotification(string title, string message, NotificationType type)
    {
        _notifications.Insert(0, new NotificationMessage
        {
            Title = title,
            Message = message,
            Type = type,
            Timestamp = DateTime.Now
        });

        if (_notifications.Count > 5)
        {
            _notifications.RemoveAt(_notifications.Count - 1);
        }
        _ = InvokeAsync(StateHasChanged);
    }

    protected void RemoveNotification(NotificationMessage message)
    {
        _notifications.Remove(message);
        _ = InvokeAsync(StateHasChanged);
    }

    #endregion

    #region Supporting Types

    public class NotificationMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; } = NotificationType.Info;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    #endregion
}
