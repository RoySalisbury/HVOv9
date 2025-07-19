using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO;
using System.Timers;

namespace HVO.WebSite.RoofControllerV4.Components.Pages;

/// <summary>
/// Blazor page component for observatory roof control operations.
/// Provides a touch-friendly interface for opening, closing, and stopping the roof,
/// with real-time status updates and safety features including limit switch simulation.
/// </summary>
public partial class RoofControl : ComponentBase, IDisposable
{
    #region Dependency Injection

    [Inject] private IRoofController RoofController { get; set; } = default!;
    [Inject] private ILogger<RoofControl> Logger { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    #endregion

    #region Private Fields

    private System.Timers.Timer? _statusUpdateTimer;
    private DateTime? _operationStartTime;
    private readonly List<NotificationMessage> _notifications = new();
    private bool _isDisposed = false;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the current roof controller status.
    /// </summary>
    public RoofControllerStatus CurrentStatus { get; private set; } = RoofControllerStatus.Unknown;

    /// <summary>
    /// Gets a value indicating whether the roof controller is initialized.
    /// </summary>
    public bool IsInitialized { get; private set; } = false;

    /// <summary>
    /// Gets the time when the current operation started.
    /// </summary>
    public DateTime? OperationStartTime => _operationStartTime;

    /// <summary>
    /// Gets a value indicating whether the roof is currently moving.
    /// </summary>
    public bool IsMoving => CurrentStatus == RoofControllerStatus.Opening || CurrentStatus == RoofControllerStatus.Closing;

    /// <summary>
    /// Gets a value indicating whether the roof is open.
    /// </summary>
    public bool IsRoofOpen => CurrentStatus == RoofControllerStatus.Open;

    /// <summary>
    /// Gets a value indicating whether the Open button should be disabled.
    /// </summary>
    public bool IsOpenDisabled 
    { 
        get 
        {
            // Only disable Open button if not initialized, currently moving, or if we're already opening/opened
            // Allow opening even from "Open" status in case user wants to continue/retry operation
            var disabled = !IsInitialized || IsMoving || CurrentStatus == RoofControllerStatus.Opening;
            Logger.LogDebug("IsOpenDisabled: {Disabled} (Initialized: {Initialized}, Moving: {Moving}, Status: {Status})", 
                disabled, IsInitialized, IsMoving, CurrentStatus);
            return disabled;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Close button should be disabled.
    /// </summary>
    public bool IsCloseDisabled 
    { 
        get 
        {
            // Only disable Close button if not initialized, currently moving, or if we're already closing/closed
            // Allow closing even from "Closed" status in case user wants to continue/retry operation
            var disabled = !IsInitialized || IsMoving || CurrentStatus == RoofControllerStatus.Closing;
            Logger.LogDebug("IsCloseDisabled: {Disabled} (Initialized: {Initialized}, Moving: {Moving}, Status: {Status})", 
                disabled, IsInitialized, IsMoving, CurrentStatus);
            return disabled;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Stop button should be disabled.
    /// </summary>
    public bool IsStopDisabled => !IsInitialized || !IsMoving;

    /// <summary>
    /// Gets the list of current notification messages.
    /// </summary>
    public IReadOnlyList<NotificationMessage> Notifications => _notifications.AsReadOnly();

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

        #region Roof Control Operations    /// <summary>
    /// Initiates the roof opening operation.
    /// </summary>
    public async Task OpenRoof()
    {
        if (IsOpenDisabled) return;

        try
        {
            Logger.LogInformation("User initiated roof opening operation");
            
            var result = RoofController.Open();
            
            if (result.IsSuccessful)
            {
                _operationStartTime = DateTime.UtcNow;
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
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during roof opening operation");
            AddNotification("Error", $"Exception during open operation: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Initiates the roof closing operation.
    /// </summary>
    public async Task CloseRoof()
    {
        if (IsCloseDisabled) return;

        try
        {
            Logger.LogInformation("User initiated roof closing operation");
            
            var result = RoofController.Close();
            
            if (result.IsSuccessful)
            {
                _operationStartTime = DateTime.UtcNow;
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
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during roof closing operation");
            AddNotification("Error", $"Exception during close operation: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Stops the current roof operation immediately.
    /// </summary>
    public async Task StopRoof()
    {
        if (IsStopDisabled) return;

        try
        {
            Logger.LogInformation("User initiated emergency stop operation");
            
            var result = RoofController.Stop();
            
            if (result.IsSuccessful)
            {
                _operationStartTime = null;
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
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception during roof stop operation");
            AddNotification("Error", $"Exception during stop operation: {ex.Message}", NotificationType.Error);
        }
        }

        #endregion

        #region Status and UI Helper Methods    /// <summary>
    /// Updates the current status from the roof controller.
    /// </summary>
    private async Task UpdateStatusAsync()
    {
        try
        {
            var previousStatus = CurrentStatus;
            var previousInitialized = IsInitialized;
            
            CurrentStatus = RoofController.Status;
            IsInitialized = RoofController.IsInitialized;

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
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating roof status");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// For testing/simulation purposes - manually sets the roof status to simulate completion
    /// </summary>
    private void SimulateStatusCompletion(RoofControllerStatus previousStatus)
    {
        try
        {
            // This is a workaround for the simulation until we have proper hardware limit switch simulation
            if (previousStatus == RoofControllerStatus.Opening)
            {
                // In real hardware, this would be set by the limit switch event handler
                Logger.LogInformation("Simulating roof open status for UI demonstration");
            }
            else if (previousStatus == RoofControllerStatus.Closing)
            {
                // In real hardware, this would be set by the limit switch event handler
                Logger.LogInformation("Simulating roof closed status for UI demonstration");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during status simulation");
        }
    }

    /// <summary>
    /// Gets the operation time display string.
    /// </summary>
    public string GetOperationTimeDisplay()
    {
        if (!_operationStartTime.HasValue) return "00:00";
        
        var elapsed = DateTime.UtcNow - _operationStartTime.Value;
        return elapsed.ToString(@"mm\:ss");
    }

    /// <summary>
    /// Gets the CSS class for the current status display.
    /// </summary>
    public string GetStatusCssClass()
    {
        return CurrentStatus switch
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
    /// Gets the badge CSS class for the current status.
    /// </summary>
    public string GetStatusBadgeClass()
    {
        return CurrentStatus switch
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
    /// Gets the animation CSS class for the roof halves.
    /// </summary>
    public string GetRoofAnimationClass()
    {
        return CurrentStatus switch
        {
            RoofControllerStatus.Opening => "opening",
            RoofControllerStatus.Closing => "closing",
            _ => ""
        };
    }

    /// <summary>
    /// Gets the transform style for the left roof half.
    /// </summary>
    public string GetLeftRoofTransform()
    {
        return CurrentStatus switch
        {
            RoofControllerStatus.Open => "translateX(-50px)",
            RoofControllerStatus.Opening => "translateX(-25px)",
            RoofControllerStatus.PartiallyOpen => "translateX(-30px)",   // Partially open position
            _ => "translateX(0)"
        };
    }

    /// <summary>
    /// Gets the transform style for the right roof half.
    /// </summary>
    public string GetRightRoofTransform()
    {
        return CurrentStatus switch
        {
            RoofControllerStatus.Open => "translateX(50px)",
            RoofControllerStatus.Opening => "translateX(25px)",
            RoofControllerStatus.PartiallyOpen => "translateX(30px)",    // Partially open position
            _ => "translateX(0)"
        };
    }

    /// <summary>
    /// Gets the CSS class for the status indicator.
    /// </summary>
    public string GetStatusIndicatorClass()
    {
        return CurrentStatus switch
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

    /// <summary>
    /// Gets the toast icon for a notification type.
    /// </summary>
    public string GetToastIcon(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => "bi bi-check-circle text-success",
            NotificationType.Error => "bi bi-exclamation-triangle text-danger",
            NotificationType.Warning => "bi bi-exclamation-triangle text-warning",
            NotificationType.Info => "bi bi-info-circle text-info",
            _ => "bi bi-info-circle text-info"
        };
    }

    #endregion

    #region Enhanced Status and Safety Methods

    /// <summary>
    /// Gets the CSS class for the health check badge.
    /// </summary>
    public string GetHealthCheckBadgeClass()
    {
        // This would typically integrate with actual health check results
        // For now, we'll base it on whether the controller is initialized and functioning
        if (IsInitialized && CurrentStatus != RoofControllerStatus.Error)
        {
            return "bg-success";
        }
        else if (CurrentStatus == RoofControllerStatus.Error)
        {
            return "bg-danger";
        }
        else
        {
            return "bg-warning";
        }
    }

    /// <summary>
    /// Gets the remaining time for the safety watchdog timer.
    /// </summary>
    public TimeSpan GetSafetyWatchdogTimeRemaining()
    {
        if (!IsMoving || !OperationStartTime.HasValue)
        {
            return TimeSpan.FromSeconds(90); // Default watchdog timeout
        }

        var elapsed = DateTime.UtcNow - OperationStartTime.Value;
        var remaining = TimeSpan.FromSeconds(90) - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Gets the CSS class for the operation progress bar.
    /// </summary>
    public string GetProgressBarClass()
    {
        var remaining = GetSafetyWatchdogTimeRemaining();
        
        if (remaining.TotalSeconds > 60)
        {
            return "progress-bar-success";
        }
        else if (remaining.TotalSeconds > 30)
        {
            return "progress-bar-warning";
        }
        else
        {
            return "progress-bar-danger";
        }
    }

    /// <summary>
    /// Gets the operation progress percentage for the progress bar.
    /// </summary>
    public int GetOperationProgressPercentage()
    {
        if (!IsMoving || !OperationStartTime.HasValue)
        {
            return 0;
        }

        var elapsed = DateTime.UtcNow - OperationStartTime.Value;
        var totalSeconds = 90.0; // Safety watchdog timeout
        var percentage = (elapsed.TotalSeconds / totalSeconds) * 100;
        
        return Math.Min(100, Math.Max(0, (int)percentage));
    }

    /// <summary>
    /// Gets the health check status string for display.
    /// </summary>
    public string GetHealthCheckStatus()
    {
        if (IsInitialized && CurrentStatus != RoofControllerStatus.Error)
        {
            return "Healthy";
        }
        else if (CurrentStatus == RoofControllerStatus.Error)
        {
            return "Error Detected";
        }
        else
        {
            return "Checking...";
        }
    }

    #endregion

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
