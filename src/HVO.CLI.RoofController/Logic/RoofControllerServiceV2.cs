using System;
using HVO.CLI.RoofController.Models;
using HVO.Iot.Devices.Iot.Devices.Sequent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HVO.CLI.RoofController.Logic;

public class RoofControllerServiceV2 : IRoofControllerServiceV2, IAsyncDisposable, IDisposable
{
    protected readonly object _syncLock = new object();
    protected volatile bool _disposed;  // Make volatile for thread-safe checking

    // Safety watchdog fields - use single lock for atomicity
    protected System.Timers.Timer? _safetyWatchdogTimer;
    protected DateTime _operationStartTime;

    protected string _lastCommand = string.Empty;
    protected RoofControllerStopReason _lastStopReason = RoofControllerStopReason.None;

    private readonly ILogger<RoofControllerServiceV2> _logger;
    private readonly RoofControllerOptionsV2 _roofControllerOptions;
    private readonly FourRelayFourInputHat _fourRelayFourInputHat;

    // Forward digital input events from the HAT
    private EventHandler<bool>? _hatIn1Handler;
    private EventHandler<bool>? _hatIn2Handler;
    private EventHandler<bool>? _hatIn3Handler;
    private EventHandler<bool>? _hatIn4Handler;

    // Legacy DigitalInput1..4 events removed; use named alias events below

    /// <summary>Raised when the forward (open) limit switch changes state.</summary>
    public event EventHandler<bool>? ForwardLimitSwitchChanged;
    /// <summary>Raised when the reverse (close) limit switch changes state.</summary>
    public event EventHandler<bool>? ReverseLimitSwitchChanged;
    /// <summary>Raised when fault notification input changes state.</summary>
    public event EventHandler<bool>? FaultNotificationChanged;
    /// <summary>Raised when roof movement notification input changes state.</summary>
    public event EventHandler<bool>? RoofMovementNotificationChanged;

    public RoofControllerServiceV2(ILogger<RoofControllerServiceV2> logger, IOptions<RoofControllerOptionsV2> roofControllerOptions, FourRelayFourInputHat fourRelayFourInputHat)
    {
        this._logger = logger;
        this._roofControllerOptions = roofControllerOptions.Value;
        this._fourRelayFourInputHat = fourRelayFourInputHat;
    }

    public virtual bool IsInitialized { get; protected set; } = false;

    public virtual RoofControllerStatus Status { get; protected set; } = RoofControllerStatus.NotInitialized;

    /// <summary>
    /// Gets a value indicating whether the roof is currently moving (opening or closing).
    /// This property returns true when the roof is actively in motion and not at a limit switch position.
    /// </summary>
    public virtual bool IsMoving => Status == RoofControllerStatus.Opening || Status == RoofControllerStatus.Closing;

    /// <summary>
    /// Gets the reason for the last stop operation.
    /// </summary>
    public virtual RoofControllerStopReason LastStopReason
    {
        get
        {
            lock (_syncLock)
            {
                return _lastStopReason;
            }
        }
        protected set
        {
            lock (_syncLock)
            {
                _lastStopReason = value;
            }
        }
    }


    public virtual Task<Result<bool>> Initialize(CancellationToken cancellationToken)
    {
        if (this._disposed)
        {
            return Task.FromResult(Result<bool>.Failure(new ObjectDisposedException(nameof(RoofControllerServiceV2))));
        }

        lock (this._syncLock)
        {
            if (this.IsInitialized)
            {
                return Task.FromResult(Result<bool>.Failure(new InvalidOperationException("Already Initialized")));
            }

            // Setup the cancellation token registration so we know when things are shutting down as soon as possible and can call STOP.
            cancellationToken.Register(() => this.Stop());

            // Initialize the safety watchdog timer
            this.InitializeSafetyWatchdog();

            // Optionally set HAT poll interval
            try
            {
                _fourRelayFourInputHat.DigitalInputPollInterval = _roofControllerOptions.DigitalInputPollInterval;
            }
            catch { }

            // Subscribe to HAT input events and forward them if enabled
            if (_roofControllerOptions.EnableDigitalInputPolling)
            {
                _hatIn1Handler = (_, s) => { ForwardLimitSwitchChanged?.Invoke(this, s); };
                _hatIn2Handler = (_, s) => { ReverseLimitSwitchChanged?.Invoke(this, s); };
                _hatIn3Handler = (_, s) => { FaultNotificationChanged?.Invoke(this, s); };
                _hatIn4Handler = (_, s) => { RoofMovementNotificationChanged?.Invoke(this, s); };

                _fourRelayFourInputHat.DigitalInput1Changed += _hatIn1Handler;
                _fourRelayFourInputHat.DigitalInput2Changed += _hatIn2Handler;
                _fourRelayFourInputHat.DigitalInput3Changed += _hatIn3Handler;
                _fourRelayFourInputHat.DigitalInput4Changed += _hatIn4Handler;
            }

            // Always reset to a known safe state on initialization. Using the InternalStop will bypass the initialization check.
            this.InternalStop(RoofControllerStopReason.None);

            this.IsInitialized = true;
            return Task.FromResult(Result<bool>.Success(this.IsInitialized));
        }
    }

    /// <summary>
    /// Initializes the safety watchdog timer that prevents runaway operations.
    /// </summary>
    protected virtual void InitializeSafetyWatchdog()
    {
        lock (_syncLock)
        {
            if (_safetyWatchdogTimer != null)
            {
                _safetyWatchdogTimer.Stop();
                _safetyWatchdogTimer.Dispose();
            }

            _safetyWatchdogTimer = new System.Timers.Timer(_roofControllerOptions.SafetyWatchdogTimeout.TotalMilliseconds);
            _safetyWatchdogTimer.Elapsed += SafetyWatchdog_Elapsed;
            _safetyWatchdogTimer.AutoReset = false; // One-time trigger
        }
    }

    /// <summary>
    /// Starts the safety watchdog timer for the current operation.
    /// </summary>
    protected virtual void StartSafetyWatchdog()
    {
        lock (_syncLock)
        {
            if (_safetyWatchdogTimer != null)
            {
                // Stop and dispose the existing timer to ensure clean restart
                _safetyWatchdogTimer.Stop();
                _safetyWatchdogTimer.Dispose();

                // Create a new timer instance for reliable restart
                _safetyWatchdogTimer = new System.Timers.Timer(_roofControllerOptions.SafetyWatchdogTimeout.TotalMilliseconds);
                _safetyWatchdogTimer.Elapsed += SafetyWatchdog_Elapsed;
                _safetyWatchdogTimer.AutoReset = false; // One-time trigger

                _operationStartTime = DateTime.Now;
                _safetyWatchdogTimer.Start();
                _logger.LogInformation("Safety watchdog started for {timeout} seconds", _roofControllerOptions.SafetyWatchdogTimeout.TotalSeconds);
            }
        }
    }

    /// <summary>
    /// Stops the safety watchdog timer.
    /// </summary>
    protected virtual void StopSafetyWatchdog()
    {
        lock (_syncLock)
        {
            if (_safetyWatchdogTimer != null)
            {
                _safetyWatchdogTimer.Stop();
                var elapsed = DateTime.Now - _operationStartTime;
                _logger.LogInformation("Safety watchdog stopped after {elapsed} seconds", elapsed.TotalSeconds);

                // Note: We don't dispose the timer here since we may need to restart it
                // The timer will be disposed and recreated in StartSafetyWatchdog or during final disposal
            }
        }
    }

    /// <summary>
    /// Safety watchdog timer elapsed event handler - emergency stops the roof.
    /// Thread-safe with proper disposal checking and atomic hardware operations.
    /// </summary>
    protected virtual void SafetyWatchdog_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _logger.LogWarning("SAFETY WATCHDOG TRIGGERED: Roof operation exceeded maximum allowed time of {timeout} seconds. Emergency stopping roof.",
            _roofControllerOptions.SafetyWatchdogTimeout.TotalSeconds);

        try
        {
            // Emergency stop - bypass normal checks but check disposal
            lock (_syncLock)
            {
                // Check if we're disposed - if so, don't perform any GPIO operations
                if (IsDisposed)
                    return;

                //InternalStop(RoofControllerStopReason.SafetyWatchdogTimeout);
                Status = RoofControllerStatus.Error;
                _lastCommand = "SafetyStop";


                _logger.LogError("Roof stopped by safety watchdog - manual intervention may be required");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop roof during safety watchdog trigger");
        }
    }


    /// <summary>
    /// Finalizer (destructor) ensures cleanup of resources if Dispose is not called.
    /// Should rarely be needed as proper disposal should occur through IAsyncDisposable.
    /// </summary>
    ~RoofControllerServiceV2()
    {
        // Pass false because we're in the finalizer
        Dispose(false);
    }

    /// <summary>
    /// Core async disposal implementation that handles cleanup of all resources.
    /// This is the primary disposal logic used by both sync and async disposal paths.
    /// </summary>
    /// <returns>A ValueTask representing the async disposal operation.</returns>
    protected virtual ValueTask DisposeAsyncCore()
    {
        if (_disposed) return ValueTask.CompletedTask;

        try
        {
            // 1. Stop any ongoing operations first
            InternalStop(RoofControllerStopReason.SystemDisposal);

            // 2. Unsubscribe from HAT events
            try
            {
                if (_hatIn1Handler is not null) _fourRelayFourInputHat.DigitalInput1Changed -= _hatIn1Handler;
                if (_hatIn2Handler is not null) _fourRelayFourInputHat.DigitalInput2Changed -= _hatIn2Handler;
                if (_hatIn3Handler is not null) _fourRelayFourInputHat.DigitalInput3Changed -= _hatIn3Handler;
                if (_hatIn4Handler is not null) _fourRelayFourInputHat.DigitalInput4Changed -= _hatIn4Handler;
                _hatIn1Handler = _hatIn2Handler = _hatIn3Handler = _hatIn4Handler = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from HAT input events");
            }

            // 3. Stop and dispose the safety watchdog timer
            try
            {
                lock (_syncLock)
                {
                    if (_safetyWatchdogTimer != null)
                    {
                        _safetyWatchdogTimer.Stop();
                        _safetyWatchdogTimer.Elapsed -= SafetyWatchdog_Elapsed;
                        _safetyWatchdogTimer.Dispose();
                        _safetyWatchdogTimer = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing safety watchdog timer");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async disposal of RoofController");
            throw;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Asynchronously releases all resources used by the RoofController.
    /// This is the preferred disposal method as it properly handles async cleanup of GPIO resources.
    /// </summary>
    /// <returns>A ValueTask representing the async disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await DisposeAsyncCore().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected disposal method that implements the dispose pattern.
    /// This method handles the actual cleanup work for both disposal paths.
    /// </summary>
    /// <param name="disposing">
    /// True when called from IDisposable.Dispose, false when called from finalizer.
    /// When false, only cleanup unmanaged resources.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            try
            {
                // Use the async disposal pattern but block on it
                // This is acceptable in disposal path since we're already blocking
                DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disposal of RoofController");
                throw;
            }
        }

        _disposed = true;
    }

    /// <summary>
    /// Synchronously releases all resources used by the RoofController.
    /// This method blocks while waiting for async operations to complete.
    /// Consider using DisposeAsync for better performance.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Helper method to throw ObjectDisposedException if this instance has been disposed.
    /// Uses volatile read for thread safety.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
    protected virtual void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    // Removed service-level polling; events now forwarded from HAT

    protected virtual void UpdateRoofStatus()
    {
        lock (this._syncLock)
        {
            var openTriggered = false;  //this._roofOpenLimitSwitch?.IsTriggered ?? false;
            var closedTriggered = false;  //this._roofClosedLimitSwitch?.IsTriggered ?? false;

            if (openTriggered && !closedTriggered)
            {
                this.Status = RoofControllerStatus.Open;
            }
            else if (!openTriggered && closedTriggered)
            {
                this.Status = RoofControllerStatus.Closed;
            }
            else if (!openTriggered && !closedTriggered)
            {
                // Roof is between positions - determine based on current status and whether operation is active
                var isOperationActive = _safetyWatchdogTimer?.Enabled ?? false;

                if (this.Status == RoofControllerStatus.Opening || _lastCommand == "Open")
                {
                    if (isOperationActive)
                    {
                        // Operation is still active - keep status as Opening
                        // Don't change to PartiallyOpen until the operation actually stops
                        this._logger.LogTrace("Roof opening in progress - keeping Opening status (watchdog active)");
                    }
                    else
                    {
                        // Operation stopped - roof is partially open
                        this.Status = RoofControllerStatus.PartiallyOpen;
                        this._logger.LogDebug("Roof opening operation stopped - setting to PartiallyOpen");
                    }
                }
                else if (this.Status == RoofControllerStatus.Closing || _lastCommand == "Close")
                {
                    if (isOperationActive)
                    {
                        // Operation is still active - keep status as Closing
                        // Don't change to PartiallyClose until the operation actually stops
                        this._logger.LogTrace("Roof closing in progress - keeping Closing status (watchdog active)");
                    }
                    else
                    {
                        // Operation stopped - roof is partially closed
                        this.Status = RoofControllerStatus.PartiallyClose;
                        this._logger.LogDebug("Roof closing operation stopped - setting to PartiallyClose");
                    }
                }
                else
                {
                    // Unknown state - default to stopped
                    this.Status = RoofControllerStatus.Stopped;
                }
            }
            else if (openTriggered && closedTriggered)
            {
                // Error state - both switches triggered simultaneously
                this.Status = RoofControllerStatus.Error;
                this._logger.LogError("Both limit switches are triggered simultaneously - this indicates a hardware problem");
            }

            this._logger.LogDebug("UpdateRoofStatus: OpenTriggered={openTriggered}, ClosedTriggered={closedTriggered}, LastCommand={lastCommand}, Status={status}",
                openTriggered, closedTriggered, _lastCommand, this.Status);
        }
    }


        public virtual Result<RoofControllerStatus> Stop(RoofControllerStopReason reason = RoofControllerStopReason.NormalStop)
        {
            try
            {
                ThrowIfDisposed();

                lock (this._syncLock)
                {
                    if (this.IsInitialized == false)
                    {
                        return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Device not initialized"));
                    }

                    // Preserve the previous command for status determination, only set to "Stop" if it was empty/unknown
                    if (string.IsNullOrEmpty(this._lastCommand) || this._lastCommand == "Initialize")
                    {
                        this._lastCommand = "Stop";
                    }
                    // If _lastCommand was "Open" or "Close", keep it for proper status determination in UpdateRoofStatus()
                    
                    this.InternalStop(reason);
                    this.StopSafetyWatchdog();
                    
                    // Update status again after stopping watchdog to ensure correct status transition
                    this.UpdateRoofStatus();
                    
                    this._logger.LogInformation($"====Stop - {DateTime.Now:O}. Reason: {reason}. Current Status: {this.Status}");
                    return Result<RoofControllerStatus>.Success(this.Status);
                }
            }
            catch (Exception ex)
            {
                return Result<RoofControllerStatus>.Failure(ex);
            }
        }

    protected virtual void InternalStop(RoofControllerStopReason reason = RoofControllerStopReason.None)
    {
        lock (this._syncLock)
        {
            // Set the last stop reason for external access
            this.LastStopReason = reason;

            // DON'T set status to Stopped here - let UpdateRoofStatus determine the correct status
            this._logger.LogInformation($"====InternalStop - {DateTime.Now:O}. Reason: {reason}. Current Status: {this.Status}");

            // // Set all relays to safe state for STOP operation atomically
                SetRelayStatesAtomically(
                    stopRelay: true,  // Stop relay OFF to halt movement 
                    openRelay: false,   // Open relay ON
                    closeRelay: false, // Close relay OFF
                    clearFault: false   // Close relay OFF
                );


            // Update status based on limit switch states and last command
            this.UpdateRoofStatus();
            this._logger.LogInformation($"====InternalStop - {DateTime.Now:O}. Reason: {reason}. Final Status: {this.Status}");
        }
    }

    public virtual Result<RoofControllerStatus> Open()
    {
        try
        {
            ThrowIfDisposed();

            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Device not initialized"));
                }

                // Set the command BEFORE calling Stop() so UpdateRoofStatus has the correct context
                this._lastCommand = "Open";

                // Always stop the current action before starting a new one.
                var stopResult = this.Stop();
                if (!stopResult.IsSuccessful)
                {
                    return stopResult;
                }

                if ((this.Status == RoofControllerStatus.Open) || false /* (this._roofOpenLimitSwitch.IsTriggered) */)
                {
                    // If already open, just return
                    this.Status = RoofControllerStatus.Open;

                    this._logger.LogInformation($"====Open - {DateTime.Now:O}. Already Open. Current Status: {this.Status}");
                    return Result<RoofControllerStatus>.Success(this.Status);
                }

                // // Start the motors to open the roof atomically
                SetRelayStatesAtomically(
                    stopRelay: false,  // Stop relay OFF to halt movement 
                    openRelay: true,   // Open relay ON
                    closeRelay: false, // Close relay OFF
                    clearFault: false   // Close relay OFF
                );

                // Set the status to opening
                this.Status = RoofControllerStatus.Opening;
                // _lastCommand already set earlier before calling Stop()
                this.StartSafetyWatchdog();

                this._logger.LogInformation($"====Open - {DateTime.Now:O}. Current Status: {this.Status}");

                return Result<RoofControllerStatus>.Success(this.Status);
            }
        }
        catch (Exception ex)
        {
            return Result<RoofControllerStatus>.Failure(ex);
        }
    }

    public virtual Result<RoofControllerStatus> Close()
    {
        try
        {
            ThrowIfDisposed();

            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    return Result<RoofControllerStatus>.Failure(new InvalidOperationException("Device not initialized"));
                }

                // Set the command BEFORE calling Stop() so UpdateRoofStatus has the correct context
                this._lastCommand = "Close";

                // Always stop the current action before starting a new one.
                var stopResult = this.Stop();
                if (!stopResult.IsSuccessful)
                {
                    return stopResult;
                }

                if ((this.Status == RoofControllerStatus.Closed) || false /* (this._roofClosedLimitSwitch.IsTriggered) */)
                {
                    // If already closed, just return
                    this.Status = RoofControllerStatus.Closed;

                    this._logger.LogInformation($"====Close - {DateTime.Now:O}. Already Closed. Current Status: {this.Status}");
                    return Result<RoofControllerStatus>.Success(this.Status);
                }

                // // Start the motors to close the roof atomically
                SetRelayStatesAtomically(
                    stopRelay: false,  // Stop relay OFF to halt movement 
                    openRelay: false,   // Open relay ON
                    closeRelay: true, // Close relay OFF
                    clearFault: false   // Close relay OFF
                );


                // Set the status to closing
                this.Status = RoofControllerStatus.Closing;
                // _lastCommand already set earlier before calling Stop()
                this.StartSafetyWatchdog();

                this._logger.LogInformation($"====Close - {DateTime.Now:O}. Current Status: {this.Status}");

                return Result<RoofControllerStatus>.Success(this.Status);
            }
        }
        catch (Exception ex)
        {
            return Result<RoofControllerStatus>.Failure(ex);
        }
    }


    /// <summary>
    /// Safely sets all GPIO relay pins to the specified states atomically.
    /// This prevents hardware from being in inconsistent states due to exceptions.
    /// </summary>
    /// <param name="stopRelay">State for stop relay</param>
    /// <param name="openRelay">State for open relay</param>
    /// <param name="closeRelay">State for close relay</param>
    protected virtual void SetRelayStatesAtomically(bool stopRelay, bool openRelay, bool closeRelay, bool clearFault = false)
    {
        if (this._fourRelayFourInputHat == null || _roofControllerOptions == null)
            return;

        // Collect all pin operations to perform atomically
        var pinOperations = new List<(int realayId, bool relayValue, string relayName)>
            {
                (_roofControllerOptions.StopRelayId, stopRelay, "Stop"),
                (_roofControllerOptions.OpenRelayId, openRelay, "Open"),
                (_roofControllerOptions.CloseRelayId, closeRelay, "Close")
            };

        // Apply all operations or log failures individually to prevent partial states
        foreach (var (relayId, relayValue, relayName) in pinOperations)
        {
            try
            {
                var result = _fourRelayFourInputHat.TrySetRelayWithRetry(relayId, relayValue);
                if (result.IsFailure || result.Value == false)
                {
                    if (result.IsFailure && result.Error is not null)
                    {
                        _logger.LogError(result.Error, "Failed to set {RelayName} relay pin {Pin} to {Value} ", relayName, relayId, relayValue);
                    }
                    else
                    {
                        _logger.LogError("Failed to verify {RelayName} relay pin {Pin} to {Value}", relayName, relayId, relayValue);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set {RelayName} relay pin {Pin} to {Value}", relayName, relayId, relayValue);
            }
        }
    }

    protected virtual Result<bool> ClearFault(int pulseMs = 250)
    {
        pulseMs = Math.Max(0, pulseMs);

        try
        {
            ThrowIfDisposed();

            lock (this._syncLock)
            {
                if (this.IsInitialized == false)
                {
                    return Result<bool>.Failure(new InvalidOperationException("Device not initialized"));
                }

                InternalStop(RoofControllerStopReason.EmergencyStop);

                this._fourRelayFourInputHat.TrySetRelayWithRetry(this._roofControllerOptions.ClearFault, false);
                this._fourRelayFourInputHat.TrySetRelayWithRetry(this._roofControllerOptions.ClearFault, true);
                Task.Delay(pulseMs).Wait();
                this._fourRelayFourInputHat.TrySetRelayWithRetry(this._roofControllerOptions.ClearFault, false);

                this._logger.LogInformation($"====ClearFault - {DateTime.Now:O}. Current Status: {this.Status}");

                return true;
            }
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(ex);
        }
    }

    /// <summary>
    /// Thread-safe check if the controller is disposed without throwing.
    /// </summary>
    /// <returns>True if disposed, false otherwise</returns>
    protected virtual bool IsDisposed => _disposed;
}
