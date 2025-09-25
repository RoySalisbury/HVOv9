using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Options;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using HVO;
using System.Timers;

namespace HVO.WebSite.RoofControllerV4.Components.Pages;

/// <summary>
/// Code-behind for the modernized RoofControlV2 UI. Inherits behavior from RoofControl
/// and adds optional parameters specific to the V2 layout.
/// </summary>
public partial class RoofControlV2 : ComponentBase, IDisposable
{
    #region Dependency Injection

    [Inject] private IRoofControllerServiceV4 RoofController { get; set; } = default!;
    [Inject] private ILogger<RoofControlV2> Logger { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private IOptions<RoofControllerOptionsV4> RoofControllerOptions { get; set; } = default!;

    #endregion

    #region Private Fields

    // Removed polling timer; updates are push-based via service events
    private readonly List<NotificationMessage> _notifications = new();
    private bool _isDisposed = false;
    private RoofControllerStopReason _lastNotifiedStopReason = RoofControllerStopReason.None;
    
    // Simulation removed

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the current roof controller status from the service.
    /// </summary>
    public RoofControllerStatus CurrentStatus => RoofController.Status;

    /// <summary>
    /// Gets a value indicating whether the roof controller is initialized from the service.
    /// </summary>
    public bool IsInitialized => RoofController.IsInitialized;

    /// <summary>
    /// Gets a value indicating whether the roof is currently moving from the service.
    /// </summary>
    public bool IsMoving => RoofController.IsMoving;

    /// <summary>
    /// Gets a value indicating whether the roof is open from the service.
    /// </summary>
    public bool IsRoofOpen => RoofController.Status == RoofControllerStatus.Open;

    /// <summary>
    /// Indicates if the underlying service has been disposed (unavailable).
    /// </summary>
    public bool IsServiceDisposed => RoofController.IsServiceDisposed;

    /// <summary>
    /// Indicates if the UI can operate (service initialized and not disposed).
    /// </summary>
    public bool IsServiceAvailable => RoofController.IsInitialized && !RoofController.IsServiceDisposed;

    /// <summary>
    /// Gets a value indicating whether the Open button should be disabled using service's authoritative state.
    /// </summary>
    public bool IsOpenDisabled 
    { 
        get 
        {
            // Disable when not initialized, moving, opening, or already open
            var baseDisabled = !IsServiceAvailable || RoofController.IsMoving || 
                               RoofController.Status == RoofControllerStatus.Opening || 
                               RoofController.Status == RoofControllerStatus.Open ||
                               RoofController.Status == RoofControllerStatus.Error;
            return baseDisabled;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Close button should be disabled using service's authoritative state.
    /// </summary>
    public bool IsCloseDisabled 
    { 
        get 
        {
            // Disable when not initialized, moving, closing, or already closed
            var baseDisabled = !IsServiceAvailable || RoofController.IsMoving || 
                               RoofController.Status == RoofControllerStatus.Closing || 
                               RoofController.Status == RoofControllerStatus.Closed ||
                               RoofController.Status == RoofControllerStatus.Error;
            return baseDisabled;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Stop button should be disabled using service's authoritative state.
    /// </summary>
    public bool IsStopDisabled => !IsServiceAvailable || !RoofController.IsMoving;

    /// <summary>
    /// Gets a value indicating whether the Clear Fault button should be disabled.
    /// Disabled when not initialized or when moving (require stop first).
    /// </summary>
    public bool IsClearFaultDisabled => !IsServiceAvailable || RoofController.IsMoving;

    /// <summary>
    /// Gets the list of current notification messages.
    /// </summary>
    public IReadOnlyList<NotificationMessage> Notifications => _notifications.AsReadOnly();

    // Simulation controls removed

    // Removed extra endregion from previous simulation block

    // Simulation state properties removed

    /// <summary>
    /// Gets a value indicating whether the safety watchdog timer is currently running.
    /// </summary>
    public bool IsSafetyWatchdogRunning => RoofController.IsWatchdogActive;

    /// <summary>
    /// Gets the remaining time for the safety watchdog timer in seconds.
    /// </summary>
    public double SafetyWatchdogTimeRemaining => RoofController.WatchdogSecondsRemaining ?? 0;

    /// <summary>
    /// Gets the configured safety watchdog timeout in seconds.
    /// </summary>
    public double SafetyWatchdogTimeoutSeconds => RoofControllerOptions.Value.SafetyWatchdogTimeout.TotalSeconds;

    /// <summary>
    /// Timestamp of the last status transition from the service.
    /// </summary>
    public DateTimeOffset? LastTransitionUtc => RoofController.LastTransitionUtc;

    /// <summary>
    /// Convenience accessor for the last stop reason from the service.
    /// </summary>
    public RoofControllerStopReason LastStopReason => RoofController.LastStopReason;

    /// <summary>
    /// True when the last stop was an emergency (watchdog timeout or fault-triggered emergency).
    /// </summary>
    public bool WasEmergencyStop => LastStopReason is RoofControllerStopReason.EmergencyStop or RoofControllerStopReason.SafetyWatchdogTimeout;

    /// <summary>
    /// Human label for the last stop type grouping (Normal/Emergency).
    /// </summary>
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

    /// <summary>
    /// Bootstrap badge class for the last stop type grouping.
    /// </summary>
    public string GetLastStopTypeBadgeClass()
    {
        if (string.IsNullOrEmpty(GetLastStopTypeLabel())) return "d-none";
        return WasEmergencyStop ? "badge bg-danger text-white" : "badge bg-secondary";
    }

    #endregion

    #region Component Lifecycle

    /// <summary>
    /// Component initialization - sets up timers and gets initial status.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        try
        {
            Logger.LogInformation("RoofControl component initializing");
            
            // Initial status update
            await UpdateStatusAsync();

            // Subscribe to service status-changed events for push updates
            RoofController.StatusChanged += OnServiceStatusChanged;

            // UI initialized message (does not imply service initialization)
            AddNotification("UI", "Roof control UI loaded", NotificationType.Info);
            _lastNotifiedStopReason = RoofController.LastStopReason;
            if (IsServiceDisposed)
            {
                AddNotification("Service", "Roof controller service is unavailable (disposed).", NotificationType.Error);
            }
            else if (!IsInitialized)
            {
                AddNotification("Service", "Waiting for roof controller service to initialize...", NotificationType.Info);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing RoofControl component");
            AddNotification("Error", $"Failed to initialize: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Component disposal - clean up timers and resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;

        try
        {
            RoofController.StatusChanged -= OnServiceStatusChanged;

            Logger.LogInformation("RoofControl component disposed");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error disposing RoofControl component");
        }
        finally
        {
            _isDisposed = true;
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region Timer Event Handlers

    /// <summary>
    /// Handles status update timer elapsed events.
    /// </summary>
    private async void OnServiceStatusChanged(object? sender, RoofStatusChangedEventArgs e)
    {
        if (_isDisposed) return;
        try
        {
            // Use event snapshot to detect notable changes for user feedback
            var newReason = e.Status.LastStopReason;
            if (newReason != _lastNotifiedStopReason)
            {
                // Notify only on emergency-related stops
                if (newReason is RoofControllerStopReason.EmergencyStop or RoofControllerStopReason.SafetyWatchdogTimeout)
                {
                    var title = newReason == RoofControllerStopReason.SafetyWatchdogTimeout ? "Safety Watchdog" : "Emergency Stop";
                    var msg = newReason == RoofControllerStopReason.SafetyWatchdogTimeout
                        ? "Watchdog timeout triggered an automatic emergency stop."
                        : "Fault condition triggered an emergency stop.";
                    AddNotification(title, msg, NotificationType.Warning);
                }
                _lastNotifiedStopReason = newReason;
            }

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during service StatusChanged handling");
        }
    }

    #endregion

    #region Roof Control Operations
    /// <summary>
    /// Initiates the roof opening operation.
    /// In simulation mode, starts a timer that will trigger the open limit switch.
    /// In live hardware mode, calls service method directly.
    /// </summary>
    public async Task OpenRoof()
    {
        Logger.LogInformation("OpenRoof() called - IsOpenDisabled: {IsDisabled}", IsOpenDisabled);
        
        if (IsOpenDisabled) 
        {
            Logger.LogWarning("OpenRoof() blocked - button is disabled");
            return;
        }

        try
        {
            Logger.LogInformation("User initiated roof opening operation");
            
            // Direct service call (hardware-only)
            var result = RoofController.Open();
            
            if (result.IsSuccessful)
            {
                AddNotification("Operation", "Roof opening initiated", NotificationType.Success);
                Logger.LogInformation("Roof opening operation started successfully");
            }
            else
            {
                var errorMessage = result.Error?.Message ?? "Unknown error occurred";
                AddNotification("Error", $"Failed to open roof: {errorMessage}", NotificationType.Error);
                Logger.LogError("Failed to open roof: {Error}", errorMessage);
            }

            await UpdateStatusAsync();
            await InvokeAsync(StateHasChanged); // Force UI update to reflect new button states
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during roof opening operation");
            AddNotification("Error", $"Exception during open operation: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Initiates the roof closing operation.
    /// In simulation mode, starts a timer that will trigger the close limit switch.
    /// In live hardware mode, calls service method directly.
    /// </summary>
    public async Task CloseRoof()
    {
    Logger.LogInformation("CloseRoof() called - IsCloseDisabled: {IsDisabled}", IsCloseDisabled);
        
        if (IsCloseDisabled) 
        {
            Logger.LogWarning("CloseRoof() blocked - button is disabled");
            return;
        }

        try
        {
            Logger.LogInformation("User initiated roof closing operation");
            
            // Direct service call (hardware-only)
            var result = RoofController.Close();
            
            if (result.IsSuccessful)
            {
                AddNotification("Operation", "Roof closing initiated", NotificationType.Success);
                Logger.LogInformation("Roof closing operation started successfully");
            }
            else
            {
                var errorMessage = result.Error?.Message ?? "Unknown error occurred";
                AddNotification("Error", $"Failed to close roof: {errorMessage}", NotificationType.Error);
                Logger.LogError("Failed to close roof: {Error}", errorMessage);
            }

            await UpdateStatusAsync();
            await InvokeAsync(StateHasChanged); // Force UI update to reflect new button states
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during roof closing operation");
            AddNotification("Error", $"Exception during close operation: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Stops the current roof operation immediately.
    /// In simulation mode, simulates hardware button press event.
    /// In live hardware mode, calls service method directly.
    /// </summary>
    public async Task StopRoof()
    {
        if (IsStopDisabled) return;

        try
        {
            Logger.LogInformation("User initiated stop operation (NormalStop)");

            // Direct service call (hardware-only) with explicit NormalStop reason
            var result = RoofController.Stop(RoofControllerStopReason.NormalStop);
            
            if (result.IsSuccessful)
            {
                AddNotification("Stop", "All roof movement stopped", NotificationType.Info);
                Logger.LogInformation("Stop operation completed successfully");
            }
            else
            {
                var errorMessage = result.Error?.Message ?? "Unknown error occurred";
                AddNotification("Error", $"Failed to stop roof: {errorMessage}", NotificationType.Error);
                Logger.LogError("Failed to stop roof: {Error}", errorMessage);
            }

            await UpdateStatusAsync();
            await InvokeAsync(StateHasChanged); // Force UI update to reflect new button states
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during roof stop operation");
            AddNotification("Error", $"Exception during stop operation: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Pulses the clear-fault relay to reset controller/motor fault.
    /// </summary>
    public async Task ClearFaultAsync()
    {
        if (IsClearFaultDisabled)
        {
            Logger.LogWarning("ClearFaultAsync() blocked - button is disabled");
            return;
        }

        try
        {
            // optional confirm prompt
            var confirmed = await JSRuntime.InvokeAsync<bool>("confirm", "Clear controller fault now? Ensure it is safe to proceed.");
            if (!confirmed)
            {
                Logger.LogInformation("Clear fault cancelled by user");
                return;
            }
            Logger.LogInformation("User initiated clear-fault operation");
            var result = await RoofController.ClearFault();

            if (result.IsSuccessful)
            {
                AddNotification("Maintenance", "Fault clear pulse sent", NotificationType.Info);
                Logger.LogInformation("Clear fault pulse succeeded");
            }
            else
            {
                var errorMessage = result.Error?.Message ?? "Unknown error occurred";
                AddNotification("Error", $"Failed to clear fault: {errorMessage}", NotificationType.Error);
                Logger.LogError("Failed to clear fault: {Error}", errorMessage);
            }

            await UpdateStatusAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during clear fault operation");
            AddNotification("Error", $"Exception during clear fault: {ex.Message}", NotificationType.Error);
        }
    }

    #endregion

    #region Status and UI Helper Methods
    /// <summary>
    /// Updates the current status from the roof controller.
    /// </summary>
    private async Task UpdateStatusAsync()
    {
        try
        {
            var previousStatus = CurrentStatus;
            var previousInitialized = IsInitialized;
            var previousAvailability = IsServiceAvailable;

            // Log status changes for debugging
            if (previousStatus != CurrentStatus)
            {
                Logger.LogInformation("Roof status changed from {PreviousStatus} to {CurrentStatus}", 
                    previousStatus, CurrentStatus);
            }
            
            if (previousInitialized != IsInitialized)
            {
                Logger.LogInformation("Roof initialization status changed from {PreviousInitialized} to {CurrentInitialized}", 
                    previousInitialized, IsInitialized);
            }

            // Trigger UI update if status or initialization state changed
            if (previousStatus != CurrentStatus || previousInitialized != IsInitialized || previousAvailability != IsServiceAvailable)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating roof status");
        }

        await Task.CompletedTask;
    }

    // Removed reflection-based watchdog helpers; now sourced from service properties

    // Simulation completion helpers removed

    /// <summary>
    /// Gets the CSS class for the current status display using service's authoritative state.
    /// </summary>
    public string GetStatusCssClass()
    {
        return RoofController.Status switch
        {
            RoofControllerStatus.Stopped => "status-stopped",
            RoofControllerStatus.Opening => "status-opening",
            RoofControllerStatus.Closing => "status-closing",
            RoofControllerStatus.Open => "status-open",
            RoofControllerStatus.Closed => "status-closed",
            RoofControllerStatus.Error => "status-error",
            _ => "status-unknown"
        };
    }

    /// <summary>
    /// Gets the badge CSS class for the current status using service's authoritative state.
    /// </summary>
    public string GetStatusBadgeClass()
    {
        return RoofController.Status switch
        {
            RoofControllerStatus.Stopped => "bg-secondary",
            RoofControllerStatus.Opening => "bg-success",
            RoofControllerStatus.Closing => "bg-warning",
            RoofControllerStatus.Open => "bg-info",
            RoofControllerStatus.Closed => "bg-primary",
            RoofControllerStatus.PartiallyOpen => "bg-info-subtle",      // Light blue for partially open
            RoofControllerStatus.PartiallyClose => "bg-primary-subtle",  // Light primary for partially closed
            RoofControllerStatus.Error => "bg-danger",
            _ => "bg-dark"
        };
    }

    /// <summary>
    /// Gets the animation CSS class for the roof halves using service's authoritative state.
    /// </summary>
    public string GetRoofAnimationClass()
    {
        return RoofController.Status switch
        {
            RoofControllerStatus.Opening => "opening",
            RoofControllerStatus.Closing => "closing",
            _ => ""
        };
    }

    /// <summary>
    /// Gets the transform style for the left roof half using service's authoritative state.
    /// </summary>
    public string GetLeftRoofTransform()
    {
        return RoofController.Status switch
        {
            RoofControllerStatus.Open => "translateX(-50px)",
            RoofControllerStatus.Opening => "translateX(-25px)",
            RoofControllerStatus.PartiallyOpen => "translateX(-30px)",   // Partially open position
            _ => "translateX(0)"
        };
    }

    /// <summary>
    /// Gets the transform style for the right roof half using service's authoritative state.
    /// </summary>
    public string GetRightRoofTransform()
    {
        return RoofController.Status switch
        {
            RoofControllerStatus.Open => "translateX(50px)",
            RoofControllerStatus.Opening => "translateX(25px)",
            RoofControllerStatus.PartiallyOpen => "translateX(30px)",    // Partially open position
            _ => "translateX(0)"
        };
    }

    /// <summary>
    /// Gets the CSS class for the status indicator using service's authoritative state.
    /// </summary>
    public string GetStatusIndicatorClass()
    {
        return RoofController.Status switch
        {
            RoofControllerStatus.Open => "status-open",
            RoofControllerStatus.Closed => "status-closed",
            RoofControllerStatus.Opening => "status-opening",
            RoofControllerStatus.Closing => "status-closing",
            RoofControllerStatus.PartiallyOpen => "status-partially-open",
            RoofControllerStatus.PartiallyClose => "status-partially-closed",
            RoofControllerStatus.Stopped => "status-stopped",
            RoofControllerStatus.Error => "status-error",
            _ => "status-unknown"
        };
    }

    /// <summary>
    /// Gets the CSS class for the Open button including blinking animation when appropriate.
    /// Uses service's authoritative state for consistency.
    /// </summary>
    public string GetOpenButtonClass()
    {
        var baseClasses = "btn btn-success btn-lg control-btn";
        
        if (IsOpenDisabled)
        {
            Logger.LogDebug("GetOpenButtonClass: Disabled - Status: {Status}, IsMoving: {IsMoving}", RoofController.Status, RoofController.IsMoving);
            return $"{baseClasses} disabled";
        }
        
        // Blink when roof is opening to show active operation, or when safety watchdog is running
        if (RoofController.Status == RoofControllerStatus.Opening || IsSafetyWatchdogRunning)
        {
            Logger.LogDebug("GetOpenButtonClass: Blinking - Status: {Status}, SafetyWatchdog: {IsSafetyWatchdog}", RoofController.Status, IsSafetyWatchdogRunning);
            return $"{baseClasses} btn-blinking";
        }
        
        Logger.LogDebug("GetOpenButtonClass: Normal - Status: {Status}", RoofController.Status);
        return baseClasses;
    }

    /// <summary>
    /// Gets the CSS class for the Close button including blinking animation when appropriate.
    /// Uses service's authoritative state for consistency.
    /// </summary>
    public string GetCloseButtonClass()
    {
        var baseClasses = "btn btn-warning btn-lg control-btn";
        
        if (IsCloseDisabled)
        {
            Logger.LogDebug("GetCloseButtonClass: Disabled - Status: {Status}, IsMoving: {IsMoving}", RoofController.Status, RoofController.IsMoving);
            return $"{baseClasses} disabled";
        }
        
        // Blink when roof is closing to show active operation, or when safety watchdog is running
        if (RoofController.Status == RoofControllerStatus.Closing || IsSafetyWatchdogRunning)
        {
            Logger.LogDebug("GetCloseButtonClass: Blinking - Status: {Status}, SafetyWatchdog: {IsSafetyWatchdog}", RoofController.Status, IsSafetyWatchdogRunning);
            return $"{baseClasses} btn-blinking";
        }
        
        Logger.LogDebug("GetCloseButtonClass: Normal - Status: {Status}", RoofController.Status);
        return baseClasses;
    }

    /// <summary>
    /// Gets the CSS class for the Stop button including blinking animation when appropriate.
    /// Uses service's authoritative state for consistency.
    /// </summary>
    public string GetStopButtonClass()
    {
        var baseClasses = "btn btn-danger btn-lg control-btn";
        
        if (IsStopDisabled)
        {
            Logger.LogDebug("GetStopButtonClass: Disabled - Status: {Status}, IsMoving: {IsMoving}", RoofController.Status, RoofController.IsMoving);
            return $"{baseClasses} disabled";
        }
        
        // Urgent blinking when roof is moving or safety watchdog is running (emergency situations)
        if (RoofController.IsMoving || IsSafetyWatchdogRunning)
        {
            Logger.LogDebug("GetStopButtonClass: Urgent Blinking - Status: {Status}, IsMoving: {IsMoving}, SafetyWatchdog: {IsSafetyWatchdog}", RoofController.Status, RoofController.IsMoving, IsSafetyWatchdogRunning);
            return $"{baseClasses} btn-urgent-blink";
        }
        
        Logger.LogDebug("GetStopButtonClass: Normal - Status: {Status}, IsMoving: {IsMoving}", RoofController.Status, RoofController.IsMoving);
        return baseClasses;
    }

    #endregion

    #region Notification System

    /// <summary>
    /// Adds a notification message to the UI.
    /// </summary>
    private void AddNotification(string title, string message, NotificationType type)
    {
        try
        {
            var notification = new NotificationMessage
            {
                Id = Guid.NewGuid(),
                Title = title,
                Message = message,
                Type = type,
                Timestamp = DateTime.Now
            };

            _notifications.Insert(0, notification);

            // Keep only the last 5 notifications
            while (_notifications.Count > 5)
            {
                _notifications.RemoveAt(_notifications.Count - 1);
            }

            // Auto-remove success and info notifications after 5 seconds
            if (type == NotificationType.Success || type == NotificationType.Info)
            {
                _ = Task.Delay(5000).ContinueWith(_ => RemoveNotification(notification));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding notification");
        }
    }

    /// <summary>
    /// Removes a notification from the UI.
    /// </summary>
    public void RemoveNotification(NotificationMessage notification)
    {
        try
        {
            _notifications.Remove(notification);
            InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error removing notification");
        }
    }

    /// <summary>
    /// Gets the toast CSS class for a notification type.
    /// </summary>
    public string GetToastClass(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => "toast-success",
            NotificationType.Error => "toast-error",
            NotificationType.Warning => "toast-warning",
            NotificationType.Info => "toast-info",
            _ => "toast-info"
        };
    }

    public string GetToastStyle(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => "background-color: rgba(25, 135, 84, 0.9); color: white;",
            NotificationType.Error => "background-color: rgba(220, 53, 69, 0.9); color: white;",
            NotificationType.Warning => "background-color: rgba(255, 193, 7, 0.9); color: black;",
            NotificationType.Info => "background-color: rgba(13, 110, 253, 0.9); color: white;",
            _ => "background-color: rgba(108, 117, 125, 0.9); color: white;"
        };
    }

    public string GetToastIcon(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => "bi bi-check-circle-fill",
            NotificationType.Error => "bi bi-x-circle-fill",
            NotificationType.Warning => "bi bi-exclamation-triangle-fill",
            NotificationType.Info => "bi bi-info-circle-fill",
            _ => "bi bi-info-circle"
        };
    }

    public string GetHealthCheckBadgeClass()
    {
        if (IsServiceDisposed || RoofController.Status == RoofControllerStatus.Error)
        {
            return "bg-danger";
        }

        if (IsServiceAvailable)
        {
            return "bg-success";
        }

        return "bg-secondary";
    }

    /// <summary>
    /// Gets the CSS class for the operation progress bar.
    /// </summary>
    public string GetProgressBarClass()
    {
        // Simplified progress bar without timing
        return IsMoving ? "progress-bar-warning" : "progress-bar-success";
    }

    /// <summary>
    /// Gets the operation progress percentage for the progress bar.
    /// </summary>
    public int GetOperationProgressPercentage()
    {
        // Simplified progress without timing
        return IsMoving ? 50 : 0;
    }

    /// <summary>
    /// Gets the health check status string for display using service's authoritative state.
    /// </summary>
    public string GetHealthCheckStatus()
    {
        if (RoofController.IsInitialized && RoofController.Status != RoofControllerStatus.Error)
        {
            return "Healthy";
        }
        else if (RoofController.Status == RoofControllerStatus.Error)
        {
            return "Error Detected";
        }
        else
        {
            return "Checking...";
        }
    }

    /// <summary>
    /// Returns a Bootstrap badge class depending on the watchdog remaining time.
    /// </summary>
    public string GetWatchdogBadgeClass()
    {
        var remaining = SafetyWatchdogTimeRemaining;
        if (IsSafetyWatchdogRunning && remaining <= 5)
        {
            return "badge bg-danger text-white fs-6";
        }
        return "badge bg-warning text-dark fs-6";
    }

    /// <summary>
    /// Returns the progress bar class for watchdog remaining time (danger near timeout).
    /// </summary>
    public string GetWatchdogProgressBarClass()
    {
        var remaining = SafetyWatchdogTimeRemaining;
        if (IsSafetyWatchdogRunning && remaining <= 5)
        {
            return "progress-bar bg-danger progress-bar-striped progress-bar-animated";
        }
        return "progress-bar bg-warning progress-bar-striped progress-bar-animated";
    }

    /// <summary>
    /// Human-friendly relative time for last transition.
    /// </summary>
    public string GetLastTransitionFriendly()
    {
        if (LastTransitionUtc is null) return "n/a";
        var delta = DateTimeOffset.UtcNow - LastTransitionUtc.Value;
        if (delta.TotalSeconds < 60) return $"{Math.Floor(delta.TotalSeconds)}s ago";
        if (delta.TotalMinutes < 60) return $"{Math.Floor(delta.TotalMinutes)}m ago";
        if (delta.TotalHours < 24) return $"{Math.Floor(delta.TotalHours)}h ago";
        return LastTransitionUtc.Value.ToLocalTime().ToString("g");
    }

    /// <summary>
    /// Full tooltip text for last transition (UTC).
    /// </summary>
    public string GetLastTransitionTooltip()
    {
        return LastTransitionUtc?.ToString("u") ?? "unknown";
    }

    #endregion

    // Simulation control methods removed

    #region Supporting Classes

    /// <summary>
    /// Represents a notification message in the UI.
    /// </summary>
    public class NotificationMessage
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Defines the types of notifications.
    /// </summary>
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    #endregion
}
