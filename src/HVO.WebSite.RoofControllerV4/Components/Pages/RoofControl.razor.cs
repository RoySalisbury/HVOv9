using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Options;
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

    [Inject] private IRoofControllerService RoofController { get; set; } = default!;
    [Inject] private ILogger<RoofControl> Logger { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private IOptions<RoofControllerOptions> RoofControllerOptions { get; set; } = default!;

    #endregion

    #region Private Fields

    private System.Timers.Timer? _statusUpdateTimer;
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
            // Base conditions: not initialized, currently moving, or already opening
            var baseDisabled = !IsInitialized || IsMoving || CurrentStatus == RoofControllerStatus.Opening;
            
            // In simulation mode, also consider limit switch states
            if (ShowSimulationControls)
            {
                // Disable open if the open limit switch is already triggered (roof is already open)
                var limitSwitchDisabled = IsOpenLimitSwitchTriggered;
                var disabled = baseDisabled || limitSwitchDisabled;
                
                Logger.LogDebug("IsOpenDisabled: {Disabled} (Base: {BaseDisabled}, LimitSwitch: {LimitSwitchDisabled}, " +
                    "Initialized: {Initialized}, Moving: {Moving}, Status: {Status}, OpenLimitTriggered: {OpenLimit})", 
                    disabled, baseDisabled, limitSwitchDisabled, IsInitialized, IsMoving, CurrentStatus, IsOpenLimitSwitchTriggered);
                
                return disabled;
            }
            else
            {
                // In real hardware mode, use original logic
                Logger.LogDebug("IsOpenDisabled: {Disabled} (Initialized: {Initialized}, Moving: {Moving}, Status: {Status})", 
                    baseDisabled, IsInitialized, IsMoving, CurrentStatus);
                
                return baseDisabled;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Close button should be disabled.
    /// </summary>
    public bool IsCloseDisabled 
    { 
        get 
        {
            // Base conditions: not initialized, currently moving, or already closing
            var baseDisabled = !IsInitialized || IsMoving || CurrentStatus == RoofControllerStatus.Closing;
            
            // In simulation mode, also consider limit switch states
            if (ShowSimulationControls)
            {
                // Disable close if the closed limit switch is already triggered (roof is already closed)
                var limitSwitchDisabled = IsClosedLimitSwitchTriggered;
                var disabled = baseDisabled || limitSwitchDisabled;
                
                Logger.LogDebug("IsCloseDisabled: {Disabled} (Base: {BaseDisabled}, LimitSwitch: {LimitSwitchDisabled}, " +
                    "Initialized: {Initialized}, Moving: {Moving}, Status: {Status}, ClosedLimitTriggered: {ClosedLimit})", 
                    disabled, baseDisabled, limitSwitchDisabled, IsInitialized, IsMoving, CurrentStatus, IsClosedLimitSwitchTriggered);
                
                return disabled;
            }
            else
            {
                // In real hardware mode, use original logic
                Logger.LogDebug("IsCloseDisabled: {Disabled} (Initialized: {Initialized}, Moving: {Moving}, Status: {Status})", 
                    baseDisabled, IsInitialized, IsMoving, CurrentStatus);
                
                return baseDisabled;
            }
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

    /// <summary>
    /// Gets a value indicating whether simulation controls should be shown.
    /// </summary>
    public bool ShowSimulationControls => RoofControllerOptions.Value.UseSimulatedEvents;

    #endregion

    #region Simulation State Properties

    /// <summary>
    /// Gets or sets a value indicating whether the simulated open button is pressed.
    /// </summary>
    public bool IsOpenButtonPressed { get; private set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the simulated close button is pressed.
    /// </summary>
    public bool IsCloseButtonPressed { get; private set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the simulated stop button is pressed.
    /// </summary>
    public bool IsStopButtonPressed { get; private set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the simulated open limit switch is triggered.
    /// </summary>
    public bool IsOpenLimitSwitchTriggered { get; private set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the simulated closed limit switch is triggered.
    /// </summary>
    public bool IsClosedLimitSwitchTriggered { get; private set; } = false;

    /// <summary>
    /// Gets a value indicating whether the simulation timer is currently running.
    /// </summary>
    public bool IsSimulationTimerRunning => 
        (RoofController as RoofControllerServiceWithSimulatedEvents)?.IsSimulationTimerRunning ?? false;

    /// <summary>
    /// Gets the remaining time for the simulation timer.
    /// </summary>
    public double SimulationTimeRemaining =>
        (RoofController as RoofControllerServiceWithSimulatedEvents)?.SimulationTimeRemaining ?? 0;

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

            // Synchronize limit switch states with roof operational state (if using simulation)
            if (ShowSimulationControls)
            {
                SynchronizeLimitSwitchStates();
            }

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
    /// Synchronizes the limit switch simulation states with the actual roof operational state.
    /// </summary>
    private void SynchronizeLimitSwitchStates()
    {
        try
        {
            var previousOpenState = IsOpenLimitSwitchTriggered;
            var previousClosedState = IsClosedLimitSwitchTriggered;

            // Try to get the actual limit switch states from the roof controller
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService != null)
            {
                // In simulation mode, handle movement states specially
                if (CurrentStatus == RoofControllerStatus.Opening || CurrentStatus == RoofControllerStatus.Closing)
                {
                    // During movement, ensure both limit switches are not triggered in the simulation
                    // This allows the user to manually trigger them when the roof reaches the end position
                    var actualOpenTriggered = GetActualOpenLimitSwitchState();
                    var actualClosedTriggered = GetActualClosedLimitSwitchState();
                    
                    // If either limit switch is currently triggered during movement, clear it
                    if (actualOpenTriggered)
                    {
                        simulatedService.SimulateOpenLimitSwitchReleased();
                        Logger.LogDebug("Cleared open limit switch during movement - Status: {Status}", CurrentStatus);
                    }
                    if (actualClosedTriggered)
                    {
                        simulatedService.SimulateClosedLimitSwitchReleased();
                        Logger.LogDebug("Cleared closed limit switch during movement - Status: {Status}", CurrentStatus);
                    }
                    
                    // Update UI to reflect movement state (both switches not triggered)
                    IsOpenLimitSwitchTriggered = false;
                    IsClosedLimitSwitchTriggered = false;
                    
                    Logger.LogDebug("Movement state synchronized - Both limit switches cleared for roof movement. Status: {Status}", CurrentStatus);
                }
                else
                {
                    // For non-movement states, read the actual simulated hardware state
                    // This allows the UI to reflect what the user has manually set via simulation buttons
                    var actualOpenTriggered = GetActualOpenLimitSwitchState();
                    var actualClosedTriggered = GetActualClosedLimitSwitchState();
                    
                    // Update UI state to match actual hardware state
                    IsOpenLimitSwitchTriggered = actualOpenTriggered;
                    IsClosedLimitSwitchTriggered = actualClosedTriggered;
                    
                    Logger.LogDebug("Synchronized limit switch states from hardware - Open: {OpenState}, Closed: {ClosedState}, Status: {Status}", 
                        actualOpenTriggered, actualClosedTriggered, CurrentStatus);
                }
                
                Logger.LogDebug("Synchronizing limit switch states - Before: Open={PrevOpen}, Closed={PrevClosed} | After: Open={NewOpen}, Closed={NewClosed} | Status={Status}", 
                    previousOpenState, previousClosedState, IsOpenLimitSwitchTriggered, IsClosedLimitSwitchTriggered, CurrentStatus);
            }
            else
            {
                Logger.LogDebug("Not in simulation mode - using operational status for synchronization. Status: {Status}", CurrentStatus);
                
                // In real hardware mode, synchronize with operational status
                switch (CurrentStatus)
                {
                    case RoofControllerStatus.Open:
                        IsOpenLimitSwitchTriggered = true;
                        IsClosedLimitSwitchTriggered = false;
                        break;
                        
                    case RoofControllerStatus.Closed:
                        IsOpenLimitSwitchTriggered = false;
                        IsClosedLimitSwitchTriggered = true;
                        break;
                        
                    case RoofControllerStatus.PartiallyOpen:
                    case RoofControllerStatus.PartiallyClose:
                    case RoofControllerStatus.Stopped:
                        IsOpenLimitSwitchTriggered = false;
                        IsClosedLimitSwitchTriggered = false;
                        break;
                        
                    case RoofControllerStatus.Opening:
                    case RoofControllerStatus.Closing:
                        // During movement, limit switches should not be triggered yet
                        // The user will manually trigger them when the roof reaches the end position
                        IsOpenLimitSwitchTriggered = false;
                        IsClosedLimitSwitchTriggered = false;
                        break;
                        
                    case RoofControllerStatus.Error:
                        // Error state - maintain current limit switch states
                        break;
                        
                    default:
                        // For unknown states, clear both limit switches
                        IsOpenLimitSwitchTriggered = false;
                        IsClosedLimitSwitchTriggered = false;
                        break;
                }
            }

            // Log synchronization events for debugging
            if (previousOpenState != IsOpenLimitSwitchTriggered)
            {
                Logger.LogDebug("Open limit switch synchronized: {PreviousState} → {NewState} (Roof Status: {Status})", 
                    previousOpenState, IsOpenLimitSwitchTriggered, CurrentStatus);
            }
            
            if (previousClosedState != IsClosedLimitSwitchTriggered)
            {
                Logger.LogDebug("Closed limit switch synchronized: {PreviousState} → {NewState} (Roof Status: {Status})", 
                    previousClosedState, IsClosedLimitSwitchTriggered, CurrentStatus);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error synchronizing limit switch states");
        }
    }

    /// <summary>
    /// Gets the actual open limit switch state from the roof controller.
    /// </summary>
    private bool GetActualOpenLimitSwitchState()
    {
        try
        {
            // Access the protected _roofOpenLimitSwitch field using reflection
            var type = RoofController.GetType();
            System.Reflection.FieldInfo? field = null;
            
            // Try to find the field in the current type first, then base types
            var currentType = type;
            while (currentType != null && field == null)
            {
                field = currentType.GetField("_roofOpenLimitSwitch", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                currentType = currentType.BaseType;
            }
                
            if (field?.GetValue(RoofController) is HVO.Iot.Devices.GpioLimitSwitch limitSwitch)
            {
                Logger.LogDebug("Reading open limit switch state: IsTriggered={IsTriggered}", limitSwitch.IsTriggered);
                return limitSwitch.IsTriggered;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Could not get actual open limit switch state: {Error}", ex.Message);
        }
        
        // Fallback: Use current operational status
        return CurrentStatus == RoofControllerStatus.Open;
    }

    /// <summary>
    /// Gets the actual closed limit switch state from the roof controller.
    /// </summary>
    private bool GetActualClosedLimitSwitchState()
    {
        try
        {
            // Access the protected _roofClosedLimitSwitch field using reflection
            var type = RoofController.GetType();
            System.Reflection.FieldInfo? field = null;
            
            // Try to find the field in the current type first, then base types
            var currentType = type;
            while (currentType != null && field == null)
            {
                field = currentType.GetField("_roofClosedLimitSwitch", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                currentType = currentType.BaseType;
            }
                
            if (field?.GetValue(RoofController) is HVO.Iot.Devices.GpioLimitSwitch limitSwitch)
            {
                Logger.LogDebug("Reading closed limit switch state: IsTriggered={IsTriggered}", limitSwitch.IsTriggered);
                return limitSwitch.IsTriggered;
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug("Could not get actual closed limit switch state: {Error}", ex.Message);
        }
        
        // Fallback: Use current operational status
        return CurrentStatus == RoofControllerStatus.Closed;
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

    #region Simulation Control Methods

    /// <summary>
    /// Toggles the simulated open button state and triggers the appropriate event.
    /// </summary>
    private void ToggleOpenButton()
    {
        if (!ShowSimulationControls) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                AddNotification("Simulation", "Simulation controls only work with simulated service", NotificationType.Warning);
                return;
            }

            IsOpenButtonPressed = !IsOpenButtonPressed;
            
            if (IsOpenButtonPressed)
            {
                simulatedService.SimulateOpenButtonDown();
                AddNotification("Simulation", "Open button pressed (simulated)", NotificationType.Info);
            }
            else
            {
                simulatedService.SimulateOpenButtonUp();
                AddNotification("Simulation", "Open button released (simulated)", NotificationType.Info);
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error toggling open button simulation");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Toggles the simulated close button state and triggers the appropriate event.
    /// </summary>
    private void ToggleCloseButton()
    {
        if (!ShowSimulationControls) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                AddNotification("Simulation", "Simulation controls only work with simulated service", NotificationType.Warning);
                return;
            }

            IsCloseButtonPressed = !IsCloseButtonPressed;
            
            if (IsCloseButtonPressed)
            {
                simulatedService.SimulateCloseButtonDown();
                AddNotification("Simulation", "Close button pressed (simulated)", NotificationType.Info);
            }
            else
            {
                simulatedService.SimulateCloseButtonUp();
                AddNotification("Simulation", "Close button released (simulated)", NotificationType.Info);
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error toggling close button simulation");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Toggles the simulated stop button state and triggers the appropriate event.
    /// </summary>
    private void ToggleStopButton()
    {
        if (!ShowSimulationControls) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                AddNotification("Simulation", "Simulation controls only work with simulated service", NotificationType.Warning);
                return;
            }

            IsStopButtonPressed = !IsStopButtonPressed;
            
            if (IsStopButtonPressed)
            {
                simulatedService.SimulateStopButtonDown();
                AddNotification("Simulation", "Stop button pressed (simulated)", NotificationType.Info);
            }
            
            // Note: Stop button doesn't have a "release" simulation - it's momentary

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error toggling stop button simulation");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Toggles the simulated open limit switch state and triggers the appropriate event.
    /// </summary>
    private void ToggleOpenLimitSwitch()
    {
        if (!ShowSimulationControls) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                AddNotification("Simulation", "Simulation controls only work with simulated service", NotificationType.Warning);
                return;
            }

            IsOpenLimitSwitchTriggered = !IsOpenLimitSwitchTriggered;
            
            if (IsOpenLimitSwitchTriggered)
            {
                // If we're triggering the open limit switch, also clear the closed one
                if (IsClosedLimitSwitchTriggered)
                {
                    IsClosedLimitSwitchTriggered = false;
                    simulatedService.SimulateClosedLimitSwitchReleased();
                    Logger.LogDebug("Auto-released closed limit switch when open limit was triggered");
                }
                
                simulatedService.SimulateOpenLimitSwitchTriggered();
                AddNotification("Simulation", "Open limit switch triggered - Roof is now OPEN", NotificationType.Success);
            }
            else
            {
                simulatedService.SimulateOpenLimitSwitchReleased();
                AddNotification("Simulation", "Open limit switch released - Roof no longer at open position", NotificationType.Info);
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error toggling open limit switch simulation");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Toggles the simulated closed limit switch state and triggers the appropriate event.
    /// </summary>
    private void ToggleClosedLimitSwitch()
    {
        if (!ShowSimulationControls) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                AddNotification("Simulation", "Simulation controls only work with simulated service", NotificationType.Warning);
                return;
            }

            IsClosedLimitSwitchTriggered = !IsClosedLimitSwitchTriggered;
            
            if (IsClosedLimitSwitchTriggered)
            {
                // If we're triggering the closed limit switch, also clear the open one
                if (IsOpenLimitSwitchTriggered)
                {
                    IsOpenLimitSwitchTriggered = false;
                    simulatedService.SimulateOpenLimitSwitchReleased();
                    Logger.LogDebug("Auto-released open limit switch when closed limit was triggered");
                }
                
                simulatedService.SimulateClosedLimitSwitchTriggered();
                AddNotification("Simulation", "Closed limit switch triggered - Roof is now CLOSED", NotificationType.Success);
            }
            else
            {
                simulatedService.SimulateClosedLimitSwitchReleased();
                AddNotification("Simulation", "Closed limit switch released - Roof no longer at closed position", NotificationType.Info);
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error toggling closed limit switch simulation");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
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
