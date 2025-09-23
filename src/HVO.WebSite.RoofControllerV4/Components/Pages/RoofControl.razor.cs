using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Options;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using HVO;
using System.Timers;

namespace HVO.WebSite.RoofControllerV4.Components.Pages;

/// <summary>
/// Blazor page component for observatory roof control operations.
/// Provides a touch-friendly interface for opening, closing, and stopping the roof,
/// with real-time status updates and safety features.
/// </summary>
public partial class RoofControl : ComponentBase, IDisposable
{
    #region Dependency Injection

    [Inject] private IRoofControllerServiceV4 RoofController { get; set; } = default!;
    [Inject] private ILogger<RoofControl> Logger { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private IOptions<RoofControllerOptionsV4> RoofControllerOptions { get; set; } = default!;

    #endregion

    #region Private Fields

    private System.Timers.Timer? _statusUpdateTimer;
    private readonly List<NotificationMessage> _notifications = new();
    private bool _isDisposed = false;
    
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
    /// Gets a value indicating whether the Open button should be disabled using service's authoritative state.
    /// </summary>
    public bool IsOpenDisabled 
    { 
        get 
        {
            // Disable when not initialized, moving, opening, or already open
            var baseDisabled = !RoofController.IsInitialized || RoofController.IsMoving || 
                               RoofController.Status == RoofControllerStatus.Opening || 
                               RoofController.Status == RoofControllerStatus.Open;
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
            var baseDisabled = !RoofController.IsInitialized || RoofController.IsMoving || 
                               RoofController.Status == RoofControllerStatus.Closing || 
                               RoofController.Status == RoofControllerStatus.Closed;
            return baseDisabled;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Stop button should be disabled using service's authoritative state.
    /// </summary>
    public bool IsStopDisabled => !RoofController.IsInitialized || !RoofController.IsMoving;

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
    public bool IsSafetyWatchdogRunning => GetSafetyWatchdogRunningState();

    /// <summary>
    /// Gets the remaining time for the safety watchdog timer in seconds.
    /// </summary>
    public double SafetyWatchdogTimeRemaining => GetSafetyWatchdogTimeRemaining();

    /// <summary>
    /// Gets the configured safety watchdog timeout in seconds.
    /// </summary>
    public double SafetyWatchdogTimeoutSeconds => RoofControllerOptions.Value.SafetyWatchdogTimeout.TotalSeconds;

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
            
            // Get initial status
            await UpdateStatusAsync();

            // Set up status update timer (every 500ms for responsive UI)
            _statusUpdateTimer = new System.Timers.Timer(500);
            _statusUpdateTimer.Elapsed += async (sender, e) => await OnStatusUpdateTimerElapsed();
            _statusUpdateTimer.AutoReset = true;
            _statusUpdateTimer.Start();

            AddNotification("System", "Roof control interface initialized", NotificationType.Info);
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
            _statusUpdateTimer?.Stop();
            _statusUpdateTimer?.Dispose();
            _statusUpdateTimer = null;

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
    private async Task OnStatusUpdateTimerElapsed()
    {
        if (_isDisposed) return;

        try
        {
            await UpdateStatusAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during status update timer");
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
            Logger.LogInformation("User initiated emergency stop operation");
            
            // Direct service call (hardware-only)
            var result = RoofController.Stop();
            
            if (result.IsSuccessful)
            {
                AddNotification("Emergency Stop", "All roof movement stopped", NotificationType.Warning);
                Logger.LogInformation("Emergency stop operation completed successfully");
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
            if (previousStatus != CurrentStatus || previousInitialized != IsInitialized)
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

    /// <summary>
    /// Gets the safety watchdog timer running state using reflection.
    /// </summary>
    private bool GetSafetyWatchdogRunningState()
    {
        try
        {
            // Access the protected _safetyWatchdogTimer field using reflection
            var type = RoofController.GetType();
            System.Reflection.FieldInfo? field = null;
            
            // Try to find the field in the current type first, then base types
            var currentType = type;
            while (currentType != null && field == null)
            {
                field = currentType.GetField("_safetyWatchdogTimer", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                currentType = currentType.BaseType;
            }
                
            if (field?.GetValue(RoofController) is System.Timers.Timer timer)
            {
                return timer.Enabled;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not get safety watchdog timer state via reflection: {Error}", ex.Message);
        }
        
        // Fallback: Check if roof is moving (watchdog should be running during operations)
        return IsMoving;
    }

    /// <summary>
    /// Gets the safety watchdog timer remaining time using reflection.
    /// </summary>
    private double GetSafetyWatchdogTimeRemaining()
    {
        try
        {
            // Access the protected _operationStartTime field using reflection
            var type = RoofController.GetType();
            System.Reflection.FieldInfo? startTimeField = null;
            System.Reflection.FieldInfo? timerField = null;
            
            // Try to find the fields in the current type first, then base types
            var currentType = type;
            while (currentType != null && (startTimeField == null || timerField == null))
            {
                if (startTimeField == null)
                {
                    startTimeField = currentType.GetField("_operationStartTime", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                if (timerField == null)
                {
                    timerField = currentType.GetField("_safetyWatchdogTimer", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                currentType = currentType.BaseType;
            }
                
            if (startTimeField?.GetValue(RoofController) is DateTime operationStartTime && 
                timerField?.GetValue(RoofController) is System.Timers.Timer timer &&
                timer.Enabled)
            {
                var elapsed = DateTime.Now - operationStartTime;
                var timeout = TimeSpan.FromSeconds(SafetyWatchdogTimeoutSeconds);
                var remaining = timeout - elapsed;
                var remainingSeconds = Math.Max(0, remaining.TotalSeconds);
                
                return remainingSeconds;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not get safety watchdog time remaining via reflection: {Error}", ex.Message);
        }
        
        // Return 0 if not running or error
        return 0;
    }

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
        if (RoofController.Status == RoofControllerStatus.Error)
        {
            return "bg-danger";
        }

        if (RoofController.IsInitialized)
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
