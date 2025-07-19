using System.Device.Gpio;
using HVO.Iot.Devices;
using HVO.Iot.Devices.Abstractions;
using Iot.Device.Common;
using Microsoft.Extensions.Options;
using HVO;

namespace HVO.WebSite.RoofControllerV4.Logic
{
    public sealed class RoofController : IRoofController, IAsyncDisposable, IDisposable
    {
        private static PinValue RelayOff = PinValue.Low;
        private static PinValue RelayOn = PinValue.High;


        private readonly ILogger<RoofController> _logger;
        private readonly RoofControllerOptions _roofControllerOptions;

        private IGpioController _gpioController;
        private readonly bool _ownsGpioController;
        private readonly ILogger<GpioLimitSwitch> _limitSwitchLogger;
        private readonly GpioLimitSwitch _roofOpenLimitSwitch;
        private readonly GpioLimitSwitch _roofClosedLimitSwitch;

        private readonly GpioButtonWithLed _roofOpenButton;
        private readonly GpioButtonWithLed _roofCloseButton;    
        private readonly GpioButtonWithLed _roofStopButton;    

        private readonly object _syncLock = new object();
        private bool _disposed;

        public RoofController(ILogger<RoofController> logger, IOptions<RoofControllerOptions> roofControllerOptions, IGpioController gpioController)
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

        public bool IsInitialized { get; private set; } = false;

        public RoofControllerStatus Status { get; private set; } = RoofControllerStatus.NotInitialized;


        public Task<Result<bool>> Initialize(CancellationToken cancellationToken)
        {
            if (this._disposed)
            {
                return Task.FromResult(Result<bool>.Failure(new ObjectDisposedException(nameof(RoofController))));
            }

            lock (this._syncLock)
            {
                if (this.IsInitialized)
                {
                    return Task.FromResult(Result<bool>.Failure(new InvalidOperationException("Already Initialized")));
                }

                // Setup the cancellation token registration so we know when things are shutting down as soon as possible and can call STOP.
                cancellationToken.Register(() => this.Stop());

                // Always reset to a known safe state on initialization. Using the InternalStop will bypass the initialization check.
                this.InternalStop();

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


        private void roofStopButton_OnButtonDown(object? sender, EventArgs e)
        {
            this.Stop();
        }   

        private void roofCloseButton_OnButtonUp(object? sender, EventArgs e)
        {
            this.Stop();
        }   

        private void roofCloseButton_OnButtonDown(object? sender, EventArgs e)
        {
            this.Close();
        }   

        private void roofOpenButton_OnButtonUp(object? sender, EventArgs e)
        {
            this.Stop();
        }   

        private void roofOpenButton_OnButtonDown(object? sender, EventArgs e)
        {
            this.Open();
        }   
        

        private void roofOpenLimitSwitch_LimitSwitchTriggered(object? sender, LimitSwitchTriggeredEventArgs e)
        {
            if (e.ChangeType == PinEventTypes.Falling)
            {
                this.Stop();
                this.Status = RoofControllerStatus.Open;
            }
            else
            {
                this.Status = RoofControllerStatus.Closed;
            }

            this._logger.LogInformation("RoofOpenLimitSwitch: {changeType} - {eventDateTime}, CurrentStatus: {currentStatus}", e.ChangeType, e.EventDateTime, this.Status);
        }

        private void roofClosedLimitSwitch_LimitSwitchTriggered(object? sender, LimitSwitchTriggeredEventArgs e)
        {
            if (e.ChangeType == PinEventTypes.Falling)
            {
                this.Stop();
                this.Status = RoofControllerStatus.Closed;
            }
            else
            {
                this.Status = RoofControllerStatus.Opening;
            }

            this._logger.LogInformation("RoofClosedLimitSwitch: {changeType} - {eventDateTime}, CurrentStatus: {currentStatus}", e.ChangeType, e.EventDateTime, this.Status);
        }

        public Result<RoofControllerStatus> Stop()
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

                    this.InternalStop();
                    this._logger.LogInformation($"====Stop - {DateTime.Now:O}. Current Status: {this.Status}");
                    return Result<RoofControllerStatus>.Success(this.Status);
                }
            }
            catch (Exception ex)
            {
                return Result<RoofControllerStatus>.Failure(ex);
            }
        }

        private void InternalStop()
        {
            lock (this._syncLock)
            {
                this.Status = RoofControllerStatus.Stopped;
                this._logger.LogInformation($"====InternalStop - {DateTime.Now:O}. Current Status: {this.Status}");

                // Set all relays to safe state for STOP operation
                if (_gpioController != null && _roofControllerOptions != null)
                {
                    try
                    {
                        if (_gpioController.IsPinOpen(_roofControllerOptions.StopRoofRelayPin))
                        {
                            _gpioController.Write(_roofControllerOptions.StopRoofRelayPin, RelayOn); // Stop relay ON to halt movement
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set stop roof relay during stop");
                    }

                    try
                    {
                        if (_gpioController.IsPinOpen(_roofControllerOptions.OpenRoofRelayPin))
                        {
                            _gpioController.Write(_roofControllerOptions.OpenRoofRelayPin, RelayOff); // Open relay OFF
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set open roof relay during stop");
                    }

                    try
                    {
                        if (_gpioController.IsPinOpen(_roofControllerOptions.CloseRoofRelayPin))
                        {
                            _gpioController.Write(_roofControllerOptions.CloseRoofRelayPin, RelayOff); // Close relay OFF
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set close roof relay during stop");
                    }

                    try
                    {
                        if (_gpioController.IsPinOpen(_roofControllerOptions.KeypadEnableRelayPin))
                        {
                            _gpioController.Write(_roofControllerOptions.KeypadEnableRelayPin, RelayOn); // Enable keypad
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to set keypad enable relay during stop");
                    }
                }

                //Determine the state of the roof based on limit switches and/or relay pins
                if (this._roofOpenLimitSwitch.IsTriggered)
                {
                    this.Status = RoofControllerStatus.Open;
                }
                else if (this._roofClosedLimitSwitch.IsTriggered)
                {
                    this.Status = RoofControllerStatus.Closed;
                }
                else
                {
                    // If neither limit switch is triggered, we assume the roof is stopped but not in a known state
                    this.Status = RoofControllerStatus.Stopped;
                }
                this._logger.LogInformation($"====InternalStop - {DateTime.Now:O}. Final Status: {this.Status}");
            }
        }

        public Result<RoofControllerStatus> Open()
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

                    // Start the motors to open the roof
                    if (_gpioController != null && _roofControllerOptions != null)
                    {
                        if (_gpioController.IsPinOpen(_roofControllerOptions.StopRoofRelayPin))
                            _gpioController.Write(_roofControllerOptions.StopRoofRelayPin, RelayOff);
                        if (_gpioController.IsPinOpen(_roofControllerOptions.OpenRoofRelayPin))
                            _gpioController.Write(_roofControllerOptions.OpenRoofRelayPin, RelayOn);
                        if (_gpioController.IsPinOpen(_roofControllerOptions.CloseRoofRelayPin))
                            _gpioController.Write(_roofControllerOptions.CloseRoofRelayPin, RelayOff);
                        if (_gpioController.IsPinOpen(_roofControllerOptions.KeypadEnableRelayPin))
                            _gpioController.Write(_roofControllerOptions.KeypadEnableRelayPin, RelayOff);
                    }

                    // Set the status to opening
                    this.Status = RoofControllerStatus.Opening;
                    this._logger.LogInformation($"====Open - {DateTime.Now:O}. Current Status: {this.Status}");

                    return Result<RoofControllerStatus>.Success(this.Status);
                }
            }
            catch (Exception ex)
            {
                return Result<RoofControllerStatus>.Failure(ex);
            }
        }

        public Result<RoofControllerStatus> Close()
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

                    // Start the motors to close the roof
                    if (_gpioController != null && _roofControllerOptions != null)
                    {
                        if (_gpioController.IsPinOpen(_roofControllerOptions.StopRoofRelayPin))
                            _gpioController.Write(_roofControllerOptions.StopRoofRelayPin, RelayOff);
                        if (_gpioController.IsPinOpen(_roofControllerOptions.OpenRoofRelayPin))
                            _gpioController.Write(_roofControllerOptions.OpenRoofRelayPin, RelayOff);
                        if (_gpioController.IsPinOpen(_roofControllerOptions.CloseRoofRelayPin))
                            _gpioController.Write(_roofControllerOptions.CloseRoofRelayPin, RelayOn);
                        if (_gpioController.IsPinOpen(_roofControllerOptions.KeypadEnableRelayPin))
                            _gpioController.Write(_roofControllerOptions.KeypadEnableRelayPin, RelayOff);
                    }

                    // Set the status to closing
                    this.Status = RoofControllerStatus.Closing;
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
        ~RoofController()
        {
            // Pass false because we're in the finalizer
            Dispose(false);
        }

        /// <summary>
        /// Core async disposal implementation that handles cleanup of all resources.
        /// This is the primary disposal logic used by both sync and async disposal paths.
        /// </summary>
        /// <returns>A ValueTask representing the async disposal operation.</returns>
        private async ValueTask DisposeAsyncCore()
        {
            if (_disposed) return;

            try
            {
                // 1. Stop any ongoing operations first
                InternalStop();

                if (IsInitialized)
                {
                    // 2. Clean up the open limit switch
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

                    // 3. Clean up the closed limit switch
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

                    // 4. Clean up the buttons
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

                    // 5. Clean up GPIO pins (but not the controller itself if we don't own it)
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
        private void Dispose(bool disposing)
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
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}