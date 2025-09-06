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
    
    // Button state tracking for hardware simulation
    private bool _isOpenButtonPressed = false;
    private bool _isCloseButtonPressed = false;

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
            // Base conditions: not initialized, currently moving, or already opening - use service state
            var baseDisabled = !RoofController.IsInitialized || RoofController.IsMoving || RoofController.Status == RoofControllerStatus.Opening || RoofController.Status == RoofControllerStatus.Open; ;
            
            // In simulation mode, also consider limit switch states
            if (ShowSimulationControls)
            {
                // Disable open if the open limit switch is already triggered (roof is already open)
                var limitSwitchDisabled = IsOpenLimitSwitchTriggered;
                var disabled = baseDisabled || limitSwitchDisabled;
                
                // Only log state changes to avoid flooding logs
                return disabled;
            }
            else
            {
                // In real hardware mode, use service state
                return baseDisabled;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Close button should be disabled using service's authoritative state.
    /// </summary>
    public bool IsCloseDisabled 
    { 
        get 
        {
            // Base conditions: not initialized, currently moving, or already closing - use service state
            var baseDisabled = !RoofController.IsInitialized || RoofController.IsMoving || RoofController.Status == RoofControllerStatus.Closing || RoofController.Status == RoofControllerStatus.Closed;
            
            // In simulation mode, also consider limit switch states
            if (ShowSimulationControls)
            {
                // Disable close if the closed limit switch is already triggered (roof is already closed)
                var limitSwitchDisabled = IsClosedLimitSwitchTriggered;
                var disabled = baseDisabled || limitSwitchDisabled;
                
                // Only log state changes to avoid flooding logs
                return disabled;
            }
            else
            {
                // In real hardware mode, use service state
                return baseDisabled;
            }
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

    /// <summary>
    /// Gets a value indicating whether simulation controls should be shown.
    /// </summary>
    public bool ShowSimulationControls => RoofControllerOptions.Value.UseSimulatedEvents;

    #endregion

    #region Simulation State Properties

    /// <summary>
    /// Gets or sets a value indicating whether the simulated open limit switch is triggered.
    /// </summary>
    public bool IsOpenLimitSwitchTriggered { get; private set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the simulated closed limit switch is triggered.
    /// </summary>
    public bool IsClosedLimitSwitchTriggered { get; private set; } = false;

    /// <summary>
    /// Gets a value indicating whether the open button is currently pressed in simulation mode.
    /// </summary>
    public bool IsOpenButtonPressed => _isOpenButtonPressed;

    /// <summary>
    /// Gets a value indicating whether the close button is currently pressed in simulation mode.
    /// </summary>
    public bool IsCloseButtonPressed => _isCloseButtonPressed;

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

    /// <summary>
    /// Gets a value indicating whether the open limit switch timer is currently running.
    /// </summary>
    public bool IsOpenTimerRunning 
    {
        get
        {
            if (!ShowSimulationControls) return false;
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            return simulatedService?.IsSimulationTimerRunning == true && 
                   RoofController.Status == RoofControllerStatus.Opening;
        }
    }

    /// <summary>
    /// Gets the remaining time for the open limit switch timer.
    /// </summary>
    public double OpenTimerRemaining 
    {
        get
        {
            if (!IsOpenTimerRunning) return 0;
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            return simulatedService?.SimulationTimeRemaining ?? 0;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the close limit switch timer is currently running.
    /// </summary>
    public bool IsCloseTimerRunning 
    {
        get
        {
            if (!ShowSimulationControls) return false;
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            return simulatedService?.IsSimulationTimerRunning == true && 
                   RoofController.Status == RoofControllerStatus.Closing;
        }
    }

    /// <summary>
    /// Gets the remaining time for the close limit switch timer.
    /// </summary>
    public double CloseTimerRemaining 
    {
        get
        {
            if (!IsCloseTimerRunning) return 0;
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            return simulatedService?.SimulationTimeRemaining ?? 0;
        }
    }

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

        #region Roof Control Operations    /// <summary>
    /// Initiates the roof opening operation.
    /// In simulation mode, starts a timer that will trigger the open limit switch.
    /// In live hardware mode, calls service method directly.
    /// </summary>
    public async Task OpenRoof()
    {
        Logger.LogInformation("OpenRoof() called - IsOpenDisabled: {IsDisabled}, ShowSimulationControls: {ShowSim}", IsOpenDisabled, ShowSimulationControls);
        
        if (IsOpenDisabled) 
        {
            Logger.LogWarning("OpenRoof() blocked - button is disabled");
            return;
        }

        try
        {
            Logger.LogInformation("User initiated roof opening operation");
            
            if (ShowSimulationControls)
            {
                // Simulation mode: Call service button handler which will start its own simulation timer
                var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
                if (simulatedService != null)
                {
                    simulatedService.SimulateOpenButtonDown();
                    // Note: Service will handle its own simulation timer for automatic limit switch triggering
                    AddNotification("Operation", "Roof opening initiated - service will handle simulation", NotificationType.Success);
                }
                else
                {
                    Logger.LogWarning("Cannot simulate - service is not the simulated version");
                    AddNotification("Error", "Simulation not available", NotificationType.Error);
                }
            }
            else
            {
                // Live hardware mode: Direct service call
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
        Logger.LogInformation("CloseRoof() called - IsCloseDisabled: {IsDisabled}, ShowSimulationControls: {ShowSim}", IsCloseDisabled, ShowSimulationControls);
        
        if (IsCloseDisabled) 
        {
            Logger.LogWarning("CloseRoof() blocked - button is disabled");
            return;
        }

        try
        {
            Logger.LogInformation("User initiated roof closing operation");
            
            if (ShowSimulationControls)
            {
                // Simulation mode: Call service button handler which will start its own simulation timer
                var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
                if (simulatedService != null)
                {
                    simulatedService.SimulateCloseButtonDown();
                    // Note: Service will handle its own simulation timer for automatic limit switch triggering
                    AddNotification("Operation", "Roof closing initiated - service will handle simulation", NotificationType.Success);
                }
                else
                {
                    Logger.LogWarning("Cannot simulate - service is not the simulated version");
                    AddNotification("Error", "Simulation not available", NotificationType.Error);
                }
            }
            else
            {
                // Live hardware mode: Direct service call
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
            
            if (ShowSimulationControls)
            {
                // Simulation mode: Use button press event like real hardware
                var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
                if (simulatedService != null)
                {
                    // Simulate button press (stop is momentary - no release needed)
                    simulatedService.SimulateStopButtonDown();
                    Logger.LogInformation("Simulated stop button press - emergency stop triggered");
                    
                    AddNotification("Emergency Stop", "All roof movement stopped (simulated button press)", NotificationType.Warning);
                }
                else
                {
                    Logger.LogWarning("Simulation controls enabled but service is not simulated version");
                    AddNotification("Error", "Simulation mode misconfiguration", NotificationType.Error);
                }
            }
            else
            {
                // Live hardware mode: Direct service call
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

    #region Status and UI Helper Methods    /// <summary>
    /// Updates the current status from the roof controller.
    /// </summary>
    private async Task UpdateStatusAsync()
    {
        try
        {
            var previousStatus = CurrentStatus;
            var previousInitialized = IsInitialized;
            
            // Note: CurrentStatus and IsInitialized now directly use service properties
            // No need to copy values - they're always current from the service

            // Synchronize limit switch states with roof operational state (if using simulation)
            if (ShowSimulationControls)
            {
                SynchronizeLimitSwitchStates();
                // NOTE: Simulation button states are NOT synchronized with operational status
                // They should only reflect user's manual interaction with simulation controls
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
                        Logger.LogTrace("Cleared open limit switch during movement - Status: {Status}", CurrentStatus);
                    }
                    if (actualClosedTriggered)
                    {
                        simulatedService.SimulateClosedLimitSwitchReleased();
                        Logger.LogTrace("Cleared closed limit switch during movement - Status: {Status}", CurrentStatus);
                    }
                    
                    // Update UI to reflect movement state (both switches not triggered)
                    IsOpenLimitSwitchTriggered = false;
                    IsClosedLimitSwitchTriggered = false;
                    
                    Logger.LogTrace("Movement state synchronized - Both limit switches cleared for roof movement. Status: {Status}", CurrentStatus);
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
                    
                    Logger.LogTrace("Synchronized limit switch states from hardware - Open: {OpenState}, Closed: {ClosedState}, Status: {Status}", 
                        actualOpenTriggered, actualClosedTriggered, CurrentStatus);
                }
                
                // Only log if states actually changed to avoid spamming logs
                if (previousOpenState != IsOpenLimitSwitchTriggered || previousClosedState != IsClosedLimitSwitchTriggered)
                {
                    Logger.LogDebug("Limit switch states changed - Open: {PrevOpen}→{NewOpen}, Closed: {PrevClosed}→{NewClosed}, Status: {Status}", 
                        previousOpenState, IsOpenLimitSwitchTriggered, previousClosedState, IsClosedLimitSwitchTriggered, CurrentStatus);
                }
            }
            else
            {
                Logger.LogTrace("Not in simulation mode - using operational status for synchronization. Status: {Status}", CurrentStatus);
                
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
    /// This method is no longer needed since we use momentary buttons instead of toggle states.
    /// Keeping the method stub for compatibility with existing call sites.
    /// </summary>
    private void SynchronizeButtonStates()
    {
        // No longer needed - buttons are now momentary press actions without state tracking
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
                Logger.LogTrace("Reading open limit switch state: IsTriggered={IsTriggered}", limitSwitch.IsTriggered);
                return limitSwitch.IsTriggered;
            }
        }
        catch (Exception ex)
        {
            Logger.LogTrace("Could not get actual open limit switch state: {Error}", ex.Message);
        }
        
        // Fallback: Use service's authoritative operational status
        return RoofController.Status == RoofControllerStatus.Open;
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
                Logger.LogTrace("Reading closed limit switch state: IsTriggered={IsTriggered}", limitSwitch.IsTriggered);
                return limitSwitch.IsTriggered;
            }
        }
        catch (Exception ex)
        {
            Logger.LogTrace("Could not get actual closed limit switch state: {Error}", ex.Message);
        }
        
        // Fallback: Use service's authoritative operational status
        return RoofController.Status == RoofControllerStatus.Closed;
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

    /// <summary>
    /// Gets the inline style for a toast notification type (fallback for CSS issues).
    /// </summary>
    public string GetToastStyle(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => "background-color: #22c55e !important; color: white !important; border-left: 4px solid #16a34a !important;",
            NotificationType.Error => "background-color: #ef4444 !important; color: white !important; border-left: 4px solid #dc2626 !important;",
            NotificationType.Warning => "background-color: #f59e0b !important; color: white !important; border-left: 4px solid #d97706 !important;",
            NotificationType.Info => "background-color: #3b82f6 !important; color: white !important; border-left: 4px solid #2563eb !important;",
            _ => "background-color: #3b82f6 !important; color: white !important; border-left: 4px solid #2563eb !important;"
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
    /// Gets the CSS class for the health check badge using service's authoritative state.
    /// </summary>
    public string GetHealthCheckBadgeClass()
    {
        // This would typically integrate with actual health check results
        // For now, we'll base it on whether the controller is initialized and functioning
        if (RoofController.IsInitialized && RoofController.Status != RoofControllerStatus.Error)
        {
            return "bg-success";
        }
        else if (RoofController.Status == RoofControllerStatus.Error)
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

    #region Simulation Control Methods

    /// <summary>
    /// Handles pressing the open button (mousedown event).
    /// </summary>
    private async void OnOpenButtonDown()
    {
        if (!ShowSimulationControls || _isOpenButtonPressed) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                AddNotification("Simulation", "Simulation controls only work with simulated service", NotificationType.Warning);
                return;
            }

            _isOpenButtonPressed = true;
            simulatedService.SimulateOpenButtonDown();
            AddNotification("Simulation", "Open button pressed", NotificationType.Info);

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error pressing open button");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
            _isOpenButtonPressed = false;
        }
    }

    /// <summary>
    /// Handles releasing the open button (mouseup event).
    /// </summary>
    private async void OnOpenButtonUp()
    {
        if (!ShowSimulationControls || !_isOpenButtonPressed) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                return;
            }

            _isOpenButtonPressed = false;
            simulatedService.SimulateOpenButtonUp();
            AddNotification("Simulation", "Open button released", NotificationType.Info);

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error releasing open button");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
            _isOpenButtonPressed = false;
        }
    }

    /// <summary>
    /// Handles pressing the close button (mousedown event).
    /// </summary>
    private async void OnCloseButtonDown()
    {
        if (!ShowSimulationControls || _isCloseButtonPressed) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                AddNotification("Simulation", "Simulation controls only work with simulated service", NotificationType.Warning);
                return;
            }

            _isCloseButtonPressed = true;
            simulatedService.SimulateCloseButtonDown();
            AddNotification("Simulation", "Close button pressed", NotificationType.Info);

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error pressing close button");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
            _isCloseButtonPressed = false;
        }
    }

    /// <summary>
    /// Handles releasing the close button (mouseup event).
    /// </summary>
    private async void OnCloseButtonUp()
    {
        if (!ShowSimulationControls || !_isCloseButtonPressed) return;

        try
        {
            var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
            if (simulatedService == null)
            {
                Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
                return;
            }

            _isCloseButtonPressed = false;
            simulatedService.SimulateCloseButtonUp();
            AddNotification("Simulation", "Close button released", NotificationType.Info);

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error releasing close button");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
            _isCloseButtonPressed = false;
        }
    }

    /// <summary>
    /// Handles pressing the stop button (mousedown event).
    /// Stop button only has press action, no release.
    /// </summary>
    private async void OnStopButtonDown()
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

            simulatedService.SimulateStopButtonDown();
            AddNotification("Simulation", "Stop button pressed", NotificationType.Info);

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error pressing stop button");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
        }
    }

    /// <summary>
    /// Toggles the open button pressed state (for toggle-based simulation).
    /// </summary>
    private async void ToggleOpenButton()
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

            if (_isOpenButtonPressed)
            {
                // Release the button
                _isOpenButtonPressed = false;
                simulatedService.SimulateOpenButtonUp();
                AddNotification("Simulation", "Open button released", NotificationType.Info);
            }
            else
            {
                // Press the button
                _isOpenButtonPressed = true;
                simulatedService.SimulateOpenButtonDown();
                AddNotification("Simulation", "Open button pressed", NotificationType.Info);
            }

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error toggling open button");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
            _isOpenButtonPressed = false;
        }
    }

    /// <summary>
    /// Toggles the close button pressed state (for toggle-based simulation).
    /// </summary>
    private async void ToggleCloseButton()
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

            if (_isCloseButtonPressed)
            {
                // Release the button
                _isCloseButtonPressed = false;
                simulatedService.SimulateCloseButtonUp();
                AddNotification("Simulation", "Close button released", NotificationType.Info);
            }
            else
            {
                // Press the button
                _isCloseButtonPressed = true;
                simulatedService.SimulateCloseButtonDown();
                AddNotification("Simulation", "Close button pressed", NotificationType.Info);
            }

            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error toggling close button");
            AddNotification("Error", $"Simulation error: {ex.Message}", NotificationType.Error);
            _isCloseButtonPressed = false;
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
                AddNotification("Simulation", "🟢 Open limit switch triggered manually - Roof is now OPEN", NotificationType.Success);
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
                AddNotification("Simulation", "🔴 Closed limit switch triggered manually - Roof is now CLOSED", NotificationType.Success);
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

    #region Manual Limit Switch Triggering

    /// <summary>
    /// Manually triggers the open limit switch (used for manual override).
    /// </summary>
    private void TriggerOpenLimitSwitch()
    {
        if (!ShowSimulationControls) return;

        var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
        if (simulatedService == null)
        {
            Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
            return;
        }

        // Clear closed limit switch if set
        if (IsClosedLimitSwitchTriggered)
        {
            IsClosedLimitSwitchTriggered = false;
            simulatedService.SimulateClosedLimitSwitchReleased();
            Logger.LogDebug("Auto-released closed limit switch when open limit was triggered");
        }

        // Trigger open limit switch
        IsOpenLimitSwitchTriggered = true;
        simulatedService.SimulateOpenLimitSwitchTriggered();
        AddNotification("Simulation", "🟢 Open limit switch triggered manually - Roof is now OPEN", NotificationType.Success);
        
        Logger.LogInformation("Open limit switch triggered manually by user");
    }

    /// <summary>
    /// Manually triggers the close limit switch (used for manual override).
    /// </summary>
    private void TriggerCloseLimitSwitch()
    {
        if (!ShowSimulationControls) return;

        var simulatedService = RoofController as RoofControllerServiceWithSimulatedEvents;
        if (simulatedService == null)
        {
            Logger.LogWarning("Cannot trigger simulation - service is not the simulated version");
            return;
        }

        // Clear open limit switch if set
        if (IsOpenLimitSwitchTriggered)
        {
            IsOpenLimitSwitchTriggered = false;
            simulatedService.SimulateOpenLimitSwitchReleased();
            Logger.LogDebug("Auto-released open limit switch when close limit was triggered");
        }

        // Trigger close limit switch
        IsClosedLimitSwitchTriggered = true;
        simulatedService.SimulateClosedLimitSwitchTriggered();
        AddNotification("Simulation", "🔴 Close limit switch triggered manually - Roof is now CLOSED", NotificationType.Success);
        
        Logger.LogInformation("Close limit switch triggered manually by user");
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
