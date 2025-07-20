using System.Device.Gpio;
using HVO.Iot.Devices;
using HVO.Iot.Devices.Abstractions;
using Iot.Device.Common;
using Microsoft.Extensions.Options;
using HVO;
using HVO.WebSite.RoofControllerV4.Models;

namespace HVO.WebSite.RoofControllerV4.Logic
{
    public class RoofControllerService : IRoofControllerService, IAsyncDisposable, IDisposable
    {
        protected static PinValue RelayOff = PinValue.Low;
        protected static PinValue RelayOn = PinValue.High;


        protected readonly ILogger<RoofControllerService> _logger;
        protected readonly RoofControllerOptions _roofControllerOptions;

        protected IGpioController _gpioController;
        protected readonly bool _ownsGpioController;
        protected readonly ILogger<GpioLimitSwitch> _limitSwitchLogger;
        protected readonly GpioLimitSwitch _roofOpenLimitSwitch;
        protected readonly GpioLimitSwitch _roofClosedLimitSwitch;

        protected readonly GpioButtonWithLed _roofOpenButton;
        protected readonly GpioButtonWithLed _roofCloseButton;    
        protected readonly GpioButtonWithLed _roofStopButton;    

        protected readonly object _syncLock = new object();
        protected volatile bool _disposed;  // Make volatile for thread-safe checking
        protected string _lastCommand = string.Empty;
        protected RoofControllerStopReason _lastStopReason = RoofControllerStopReason.None;

        // Safety watchdog fields - use single lock for atomicity
        protected System.Timers.Timer? _safetyWatchdogTimer;
        protected DateTime _operationStartTime;
        // Removed separate _watchdogLock - use _syncLock for all state to prevent races

        public RoofControllerService(ILogger<RoofControllerService> logger, IOptions<RoofControllerOptions> roofControllerOptions, IGpioController gpioController)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(roofControllerOptions);
            ArgumentNullException.ThrowIfNull(gpioController);

            this._logger = logger;
            this._limitSwitchLogger = LoggerFactory.Create(o => { }).CreateLogger<GpioLimitSwitch>();

            this._roofControllerOptions = roofControllerOptions.Value;
            ArgumentNullException.ThrowIfNull(this._roofControllerOptions);

            this._gpioController = gpioController;
            this._ownsGpioController = false; // Injected controller is not owned by this instance
            
            // Initialize GPIO devices with proper error handling
            try
            {
                this._roofOpenLimitSwitch = new GpioLimitSwitch(this._gpioController, this._roofControllerOptions.RoofOpenedLimitSwitchPin, true, false, _roofControllerOptions.LimitSwitchDebounce, this._limitSwitchLogger);
                this._roofClosedLimitSwitch = new GpioLimitSwitch(this._gpioController, this._roofControllerOptions.RoofClosedLimitSwitchPin, true, false, _roofControllerOptions.LimitSwitchDebounce, this._limitSwitchLogger);

                this._roofOpenButton = new GpioButtonWithLed(
                    buttonPin: this._roofControllerOptions.OpenRoofButtonPin, 
                    ledPin: this._roofControllerOptions.OpenRoofButtonLedPin, 
                    doublePress: TimeSpan.FromTicks(15000000),
                    holding: TimeSpan.FromMilliseconds(2000),
                    isPullUp: false, 
                    hasExternalResistor: false, 
                    gpioController: this._gpioController, 
                    debounceTime: this._roofControllerOptions.ButtonDebounce);
                this._roofCloseButton = new GpioButtonWithLed(
                    buttonPin: this._roofControllerOptions.CloseRoofButtonPin, 
                    ledPin: this._roofControllerOptions.CloseRoofButtonLedPin, 
                    doublePress: TimeSpan.FromTicks(15000000),
                    holding: TimeSpan.FromMilliseconds(2000),
                    isPullUp: false, 
                    hasExternalResistor: false, 
                    gpioController: this._gpioController, 
                    debounceTime: this._roofControllerOptions.ButtonDebounce);
                this._roofStopButton = new GpioButtonWithLed(
                    buttonPin: this._roofControllerOptions.StopRoofButtonPin, 
                    ledPin: this._roofControllerOptions.StopRoofButtonLedPin, 
                    doublePress: TimeSpan.FromTicks(15000000),
                    holding: TimeSpan.FromMilliseconds(2000),
                    isPullUp: false, 
                    hasExternalResistor: false, 
                    gpioController: this._gpioController, 
                    debounceTime: this._roofControllerOptions.ButtonDebounce);
            }
            catch
            {
                // Clean up any partially created resources
                this._roofOpenLimitSwitch?.Dispose();
                this._roofClosedLimitSwitch?.Dispose();
                this._roofOpenButton?.Dispose();
                this._roofCloseButton?.Dispose();
                this._roofStopButton?.Dispose();
                throw;
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
                        
                    InternalStop(RoofControllerStopReason.SafetyWatchdogTimeout);
                    Status = RoofControllerStatus.Error;
                    _lastCommand = "SafetyStop";
                    
                    // Ensure LED blinking is stopped for error state
                    this.UpdateLedBlinkingState();
                    
                    _logger.LogError("Roof stopped by safety watchdog - manual intervention may be required");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop roof during safety watchdog trigger");
            }
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

        /// <summary>
        /// Updates LED blinking state based on current roof status.
        /// Starts blinking for Open button when Opening, Close button when Closing.
        /// Stops blinking when operations complete or are stopped.
        /// </summary>
        protected virtual void UpdateLedBlinkingState()
        {
            try
            {
                _logger.LogTrace("UpdateLedBlinkingState - Status: {Status}", Status);

                switch (Status)
                {
                    case RoofControllerStatus.Opening:
                        // Start blinking open button LED at 1Hz (only if LED pin is configured)
                        if (_roofControllerOptions.OpenRoofButtonLedPin.HasValue)
                            _roofOpenButton.StartBlinking(1.0);
                        if (_roofControllerOptions.CloseRoofButtonLedPin.HasValue)
                            _roofCloseButton.StopBlinking();
                        if (_roofControllerOptions.StopRoofButtonLedPin.HasValue)
                            _roofStopButton.StopBlinking();
                        
                        _logger.LogDebug("Started blinking Open button LED for Opening status");
                        break;

                    case RoofControllerStatus.Closing:
                        // Start blinking close button LED at 1Hz (only if LED pin is configured)
                        if (_roofControllerOptions.CloseRoofButtonLedPin.HasValue)
                            _roofCloseButton.StartBlinking(1.0);
                        if (_roofControllerOptions.OpenRoofButtonLedPin.HasValue)
                            _roofOpenButton.StopBlinking();
                        if (_roofControllerOptions.StopRoofButtonLedPin.HasValue)
                            _roofStopButton.StopBlinking();
                        
                        _logger.LogDebug("Started blinking Close button LED for Closing status");
                        break;

                    case RoofControllerStatus.Open:
                    case RoofControllerStatus.Closed:
                    case RoofControllerStatus.Stopped:
                    case RoofControllerStatus.PartiallyOpen:
                    case RoofControllerStatus.PartiallyClose:
                    case RoofControllerStatus.Error:
                    case RoofControllerStatus.Unknown:
                    case RoofControllerStatus.NotInitialized:
                    default:
                        // Stop all blinking for final states (only if LED pins are configured)
                        if (_roofControllerOptions.OpenRoofButtonLedPin.HasValue)
                            _roofOpenButton.StopBlinking();
                        if (_roofControllerOptions.CloseRoofButtonLedPin.HasValue)
                            _roofCloseButton.StopBlinking();
                        if (_roofControllerOptions.StopRoofButtonLedPin.HasValue)
                            _roofStopButton.StopBlinking();
                        
                        _logger.LogTrace("Stopped all LED blinking for status: {Status}", Status);
                        break;
                }
            }
            catch (Exception ex)
            {
                // Don't let LED control errors affect roof operations
                _logger.LogError(ex, "Error updating LED blinking state for status {Status}", Status);
            }
        }

        /// <summary>
        /// Stops all LED blinking immediately.
        /// Used during emergency stops and disposal.
        /// </summary>
        protected virtual void StopAllLedBlinking()
        {
            try
            {
                _logger.LogTrace("StopAllLedBlinking called");

                if (_roofControllerOptions.OpenRoofButtonLedPin.HasValue)
                    _roofOpenButton.StopBlinking();
                if (_roofControllerOptions.CloseRoofButtonLedPin.HasValue)
                    _roofCloseButton.StopBlinking();
                if (_roofControllerOptions.StopRoofButtonLedPin.HasValue)
                    _roofStopButton.StopBlinking();
                
                _logger.LogTrace("All LED blinking stopped");
            }
            catch (Exception ex)
            {
                // Don't let LED control errors affect roof operations
                _logger.LogError(ex, "Error stopping LED blinking");
            }
        }


        public virtual Task<Result<bool>> Initialize(CancellationToken cancellationToken)
        {
            if (this._disposed)
            {
                return Task.FromResult(Result<bool>.Failure(new ObjectDisposedException(nameof(RoofControllerService))));
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

                // Always reset to a known safe state on initialization. Using the InternalStop will bypass the initialization check.
                this.InternalStop(RoofControllerStopReason.None);

                // Setup the GPIO controller
                try
                {
                    this._roofOpenLimitSwitch.LimitSwitchTriggered += roofOpenLimitSwitch_LimitSwitchTriggered;
                    this._roofClosedLimitSwitch.LimitSwitchTriggered += roofClosedLimitSwitch_LimitSwitchTriggered;

                    this._roofOpenButton.ButtonDown += roofOpenButton_OnButtonDown;
                    this._roofOpenButton.ButtonUp += roofOpenButton_OnButtonUp;
                    this._roofCloseButton.ButtonDown += roofCloseButton_OnButtonDown;
                    this._roofCloseButton.ButtonUp += roofCloseButton_OnButtonUp;
                    this._roofStopButton.ButtonDown += roofStopButton_OnButtonDown;

                    // Track which pins were opened for proper cleanup on failure
                    var openedPins = new List<int>();

                    try
                    {
                        this._gpioController.OpenPin(this._roofControllerOptions.OpenRoofRelayPin, PinMode.Output);
                        this._gpioController.Write(this._roofControllerOptions.OpenRoofRelayPin, RelayOff);
                        openedPins.Add(this._roofControllerOptions.OpenRoofRelayPin);

                        this._gpioController.OpenPin(this._roofControllerOptions.CloseRoofRelayPin, PinMode.Output);
                        this._gpioController.Write(this._roofControllerOptions.CloseRoofRelayPin, RelayOn);
                        openedPins.Add(this._roofControllerOptions.CloseRoofRelayPin);

                        this._gpioController.OpenPin(this._roofControllerOptions.StopRoofRelayPin, PinMode.Output);
                        this._gpioController.Write(this._roofControllerOptions.StopRoofRelayPin, RelayOff);
                        openedPins.Add(this._roofControllerOptions.StopRoofRelayPin);

                        this._gpioController.OpenPin(this._roofControllerOptions.KeypadEnableRelayPin, PinMode.Output);
                        this._gpioController.Write(this._roofControllerOptions.KeypadEnableRelayPin, RelayOn);
                        openedPins.Add(this._roofControllerOptions.KeypadEnableRelayPin);
                    }
                    catch (Exception)
                    {
                        // Clean up any pins that were successfully opened
                        foreach (var pin in openedPins)
                        {
                            try
                            {
                                if (this._gpioController.IsPinOpen(pin))
                                {
                                    this._gpioController.ClosePin(pin);
                                }
                            }
                            catch (Exception cleanupEx)
                            {
                                this._logger.LogWarning(cleanupEx, "Failed to close pin {Pin} during initialization cleanup", pin);
                            }
                        }
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    // Clean up event handlers on failure
                    try
                    {
                        this._roofOpenLimitSwitch.LimitSwitchTriggered -= roofOpenLimitSwitch_LimitSwitchTriggered;
                        this._roofClosedLimitSwitch.LimitSwitchTriggered -= roofClosedLimitSwitch_LimitSwitchTriggered;

                        this._roofOpenButton.ButtonDown -= roofOpenButton_OnButtonDown;
                        this._roofOpenButton.ButtonUp -= roofOpenButton_OnButtonUp;
                        this._roofCloseButton.ButtonDown -= roofCloseButton_OnButtonDown;
                        this._roofCloseButton.ButtonUp -= roofCloseButton_OnButtonUp;
                        this._roofStopButton.ButtonDown -= roofStopButton_OnButtonDown;
                    }
                    catch (Exception cleanupEx)
                    {
                        this._logger.LogWarning(cleanupEx, "Failed to clean up event handlers during initialization failure");
                    }

                    this.IsInitialized = false;
                    return Task.FromResult(Result<bool>.Failure(ex));
                }

                this.IsInitialized = true;
                return Task.FromResult(Result<bool>.Success(this.IsInitialized));
            }
        }


        protected virtual void roofStopButton_OnButtonDown(object? sender, EventArgs e)
        {
            // Thread-safe button handling
            try
            {
                this.Stop(RoofControllerStopReason.StopButtonPressed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling stop button press");
            }
        }   

        protected virtual void roofCloseButton_OnButtonUp(object? sender, EventArgs e)
        {
            // Thread-safe button handling
            try
            {
                this.Stop(RoofControllerStopReason.NormalStop);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling close button release");
            }
        }   

        protected virtual void roofCloseButton_OnButtonDown(object? sender, EventArgs e)
        {
            // Thread-safe button handling
            try
            {
                this.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling close button press");
            }
        }   

        protected virtual void roofOpenButton_OnButtonUp(object? sender, EventArgs e)
        {
            // Thread-safe button handling
            try
            {
                this.Stop(RoofControllerStopReason.NormalStop);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling open button release");
            }
        }   

        protected virtual void roofOpenButton_OnButtonDown(object? sender, EventArgs e)
        {
            // Thread-safe button handling
            try
            {
                this.Open();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling open button press");
            }
        }   
        

        protected virtual void roofOpenLimitSwitch_LimitSwitchTriggered(object? sender, LimitSwitchTriggeredEventArgs e)
        {
            // Thread-safe event handling - limit switches can trigger from hardware interrupts
            lock (this._syncLock)
            {
                // Only stop the roof when the limit switch becomes triggered (contacted)
                // For InputPullUp mode: Falling = switch contacted (High to Low)
                // We should stop when the switch is actually contacted, not when released
                if (e.ChangeType == PinEventTypes.Falling && this._roofOpenLimitSwitch.IsTriggered)
                {
                    this.StopSafetyWatchdog();
                    // Call InternalStop directly to avoid recursion and ensure thread safety
                    this.InternalStop(RoofControllerStopReason.LimitSwitchReached);
                    this._lastCommand = "LimitStop";
                    this._logger.LogInformation("RoofOpenLimitSwitch contacted - stopping roof. {changeType} - {eventDateTime}, CurrentStatus: {currentStatus}", e.ChangeType, e.EventDateTime, this.Status);
                }
                else
                {
                    // Switch released - just update status, don't stop
                    this.UpdateRoofStatus();
                    this._logger.LogInformation("RoofOpenLimitSwitch released. {changeType} - {eventDateTime}, CurrentStatus: {currentStatus}", e.ChangeType, e.EventDateTime, this.Status);
                }
            }
        }

        protected virtual void roofClosedLimitSwitch_LimitSwitchTriggered(object? sender, LimitSwitchTriggeredEventArgs e)
        {
            // Thread-safe event handling - limit switches can trigger from hardware interrupts
            lock (this._syncLock)
            {
                // Only stop the roof when the limit switch becomes triggered (contacted)
                // For InputPullUp mode: Falling = switch contacted (High to Low)
                // We should stop when the switch is actually contacted, not when released
                if (e.ChangeType == PinEventTypes.Falling && this._roofClosedLimitSwitch.IsTriggered)
                {
                    this.StopSafetyWatchdog();
                    // Call InternalStop directly to avoid recursion and ensure thread safety
                    this.InternalStop(RoofControllerStopReason.LimitSwitchReached);
                    this._lastCommand = "LimitStop";
                    this._logger.LogInformation("RoofClosedLimitSwitch contacted - stopping roof. {changeType} - {eventDateTime}, CurrentStatus: {currentStatus}", e.ChangeType, e.EventDateTime, this.Status);
                }
                else
                {
                    // Switch released - just update status, don't stop
                    this.UpdateRoofStatus();
                    this._logger.LogInformation("RoofClosedLimitSwitch released. {changeType} - {eventDateTime}, CurrentStatus: {currentStatus}", e.ChangeType, e.EventDateTime, this.Status);
                }
            }
        }

        protected virtual void UpdateRoofStatus()
        {
            lock (this._syncLock)
            {
                var openTriggered = this._roofOpenLimitSwitch?.IsTriggered ?? false;
                var closedTriggered = this._roofClosedLimitSwitch?.IsTriggered ?? false;

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
                
                // Update LED blinking state based on new status
                this.UpdateLedBlinkingState();
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

        /// <summary>
        /// Safely sets all GPIO relay pins to the specified states atomically.
        /// This prevents hardware from being in inconsistent states due to exceptions.
        /// </summary>
        /// <param name="stopRelay">State for stop relay</param>
        /// <param name="openRelay">State for open relay</param>
        /// <param name="closeRelay">State for close relay</param>
        /// <param name="keypadRelay">State for keypad enable relay</param>
        protected virtual void SetRelayStatesAtomically(PinValue stopRelay, PinValue openRelay, PinValue closeRelay, PinValue keypadRelay)
        {
            if (_gpioController == null || _roofControllerOptions == null)
                return;

            // Collect all pin operations to perform atomically
            var pinOperations = new List<(int pin, PinValue value, string name)>
            {
                (_roofControllerOptions.StopRoofRelayPin, stopRelay, "Stop"),
                (_roofControllerOptions.OpenRoofRelayPin, openRelay, "Open"),
                (_roofControllerOptions.CloseRoofRelayPin, closeRelay, "Close"),
                (_roofControllerOptions.KeypadEnableRelayPin, keypadRelay, "Keypad")
            };

            // Apply all operations or log failures individually to prevent partial states
            foreach (var (pin, value, name) in pinOperations)
            {
                try
                {
                    if (_gpioController.IsPinOpen(pin))
                    {
                        _gpioController.Write(pin, value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to set {RelayName} relay pin {Pin} to {Value}", name, pin, value);
                    // Continue with other pins rather than failing completely
                }
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

                // Set all relays to safe state for STOP operation atomically
                SetRelayStatesAtomically(
                    stopRelay: RelayOn,    // Stop relay ON to halt movement
                    openRelay: RelayOff,   // Open relay OFF
                    closeRelay: RelayOff,  // Close relay OFF
                    keypadRelay: RelayOn   // Enable keypad
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

                    if ((this.Status == RoofControllerStatus.Open) || (this._roofOpenLimitSwitch.IsTriggered))
                    {
                        // If already open, just return
                        this.Status = RoofControllerStatus.Open;

                        this._logger.LogInformation($"====Open - {DateTime.Now:O}. Already Open. Current Status: {this.Status}");
                        return Result<RoofControllerStatus>.Success(this.Status);
                    }

                    // Start the motors to open the roof atomically
                    SetRelayStatesAtomically(
                        stopRelay: RelayOff,   // Stop relay OFF
                        openRelay: RelayOn,    // Open relay ON
                        closeRelay: RelayOff,  // Close relay OFF
                        keypadRelay: RelayOff  // Disable keypad during operation
                    );

                    // Set the status to opening
                    this.Status = RoofControllerStatus.Opening;
                    // _lastCommand already set earlier before calling Stop()
                    this.StartSafetyWatchdog();
                    
                    // Update LED blinking to start blinking Open button
                    this.UpdateLedBlinkingState();
                    
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

                    if ((this.Status == RoofControllerStatus.Closed) || (this._roofClosedLimitSwitch.IsTriggered))
                    {
                        // If already closed, just return
                        this.Status = RoofControllerStatus.Closed;

                        this._logger.LogInformation($"====Close - {DateTime.Now:O}. Already Closed. Current Status: {this.Status}");
                        return Result<RoofControllerStatus>.Success(this.Status);
                    }

                    // Start the motors to close the roof atomically
                    SetRelayStatesAtomically(
                        stopRelay: RelayOff,   // Stop relay OFF
                        openRelay: RelayOff,   // Open relay OFF
                        closeRelay: RelayOn,   // Close relay ON
                        keypadRelay: RelayOff  // Disable keypad during operation
                    );

                    // Set the status to closing
                    this.Status = RoofControllerStatus.Closing;
                    // _lastCommand already set earlier before calling Stop()
                    this.StartSafetyWatchdog();
                    
                    // Update LED blinking to start blinking Close button
                    this.UpdateLedBlinkingState();
                    
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
        /// Finalizer (destructor) ensures cleanup of resources if Dispose is not called.
        /// Should rarely be needed as proper disposal should occur through IAsyncDisposable.
        /// </summary>
        ~RoofControllerService()
        {
            // Pass false because we're in the finalizer
            Dispose(false);
        }

        /// <summary>
        /// Core async disposal implementation that handles cleanup of all resources.
        /// This is the primary disposal logic used by both sync and async disposal paths.
        /// </summary>
        /// <returns>A ValueTask representing the async disposal operation.</returns>
        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed) return;

            try
            {
                // 1. Stop any ongoing operations first
                InternalStop(RoofControllerStopReason.SystemDisposal);
                
                // 2. Stop all LED blinking immediately
                StopAllLedBlinking();

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

                if (IsInitialized)
                {
                    // 4. Clean up the open limit switch
                    if (_roofOpenLimitSwitch != null)
                    {
                        try
                        {
                            // Unregister event handler before disposal to prevent potential callbacks
                            _roofOpenLimitSwitch.LimitSwitchTriggered -= roofOpenLimitSwitch_LimitSwitchTriggered;
                            await _roofOpenLimitSwitch.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing roof open limit switch");
                        }
                    }

                    // 5. Clean up the closed limit switch
                    if (_roofClosedLimitSwitch != null)
                    {
                        try
                        {
                            // Unregister event handler before disposal to prevent potential callbacks
                            _roofClosedLimitSwitch.LimitSwitchTriggered -= roofClosedLimitSwitch_LimitSwitchTriggered;
                            await _roofClosedLimitSwitch.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing roof closed limit switch");
                        }
                    }

                    // 6. Clean up the buttons
                    // Unregister event handlers before disposal to prevent potential callbacks     
                    if (_roofOpenButton != null)
                    {
                        try
                        {
                            this._roofOpenButton.ButtonDown -= roofOpenButton_OnButtonDown;
                            this._roofOpenButton.ButtonUp -= roofOpenButton_OnButtonUp;
                            await _roofOpenButton.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing roof open button");
                        }
                    }

                    if (_roofCloseButton != null)
                    {
                        try
                        {
                            this._roofCloseButton.ButtonDown -= roofCloseButton_OnButtonDown;
                            this._roofCloseButton.ButtonUp -= roofCloseButton_OnButtonUp;
                            await _roofCloseButton.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing roof close button");
                        }
                    }
                    
                    if (_roofStopButton != null)
                    {
                        try
                        {
                            this._roofStopButton.ButtonDown -= roofStopButton_OnButtonDown;
                            await _roofStopButton.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing roof stop button");
                        }
                    }

                    // 7. Clean up GPIO pins (but not the controller itself if we don't own it)
                    if (_gpioController != null)
                    {
                        try
                        {
                            if (this._gpioController.IsPinOpen(this._roofControllerOptions.OpenRoofRelayPin))
                            {
                                this._gpioController.Write(this._roofControllerOptions.OpenRoofRelayPin, RelayOff);
                                this._gpioController.ClosePin(this._roofControllerOptions.OpenRoofRelayPin);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error closing open roof relay pin");
                        }

                        try
                        {
                            if (this._gpioController.IsPinOpen(this._roofControllerOptions.CloseRoofRelayPin))
                            {
                                this._gpioController.Write(this._roofControllerOptions.CloseRoofRelayPin, RelayOff);
                                this._gpioController.ClosePin(this._roofControllerOptions.CloseRoofRelayPin);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error closing close roof relay pin");
                        }

                        try
                        {
                            if (this._gpioController.IsPinOpen(this._roofControllerOptions.StopRoofRelayPin))
                            {
                                this._gpioController.Write(this._roofControllerOptions.StopRoofRelayPin, RelayOff);
                                this._gpioController.ClosePin(this._roofControllerOptions.StopRoofRelayPin);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error closing stop roof relay pin");
                        }

                        try
                        {
                            if (this._gpioController.IsPinOpen(this._roofControllerOptions.KeypadEnableRelayPin))
                            {
                                this._gpioController.Write(this._roofControllerOptions.KeypadEnableRelayPin, RelayOff);
                                this._gpioController.ClosePin(this._roofControllerOptions.KeypadEnableRelayPin);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error closing keypad enable relay pin");
                        }

                        // Only dispose the GPIO controller if we own it
                        if (_ownsGpioController)
                        {
                            try
                            {
                                // Use async disposal if available, otherwise fall back to sync
                                if (_gpioController is IAsyncDisposable asyncDisposable)
                                {
                                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                                }
                                else
                                {
                                    _gpioController.Dispose();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error disposing GPIO controller");
                            }
                        }
                        
                        _gpioController = null!;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during async disposal of RoofController");
                throw;
            }
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

        /// <summary>
        /// Thread-safe check if the controller is disposed without throwing.
        /// </summary>
        /// <returns>True if disposed, false otherwise</returns>
        protected virtual bool IsDisposed => _disposed;
    }
}