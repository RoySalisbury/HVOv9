using System.Device.Gpio;
using Iot.Device.Button;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;

namespace HVO.Iot.Devices;

/// <summary>
/// Represents a GPIO button with integrated LED functionality, supporting both synchronous and asynchronous disposal patterns.
/// Provides thread-safe operation with optimized performance for high-frequency button events.
/// </summary>
public class GpioButtonWithLed : ButtonBase, IAsyncDisposable
{
    // Copied from the original ButtonBase because they were not exposed as public/protected fields.
    internal const long DefaultDoublePressTicks = 15000000;
    internal const long DefaultHoldingMilliseconds = 2000;

    private readonly IGpioController _gpioController;
    private readonly PinMode _gpioPinMode;
    private readonly PinMode _eventPinMode;
    private readonly int _buttonPin;
    private readonly int _ledPin;
    private readonly bool _shouldDispose;
    private readonly object _lockObject = new object();

    /// <summary>
    /// Thread-safe disposal flag to prevent multiple disposal attempts.
    /// Uses volatile to ensure visibility across threads without locking.
    /// </summary>
    private volatile bool _disposed = false;
    
    /// <summary>
    /// Current LED state. Access is synchronized through _lockObject.
    /// </summary>
    private PushButtonLedState _ledState = PushButtonLedState.Off;
    
    /// <summary>
    /// Current button press state. Access is synchronized through _lockObject.
    /// </summary>
    private bool _isPressed = false;

    /// <summary>
    /// Current LED options configuration. Access is synchronized through _lockObject.
    /// </summary>
    private PushButtonLedOptions _ledOptions = PushButtonLedOptions.FollowPressedState;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpioButtonWithLed"/> class with default timing parameters.
    /// </summary>
    /// <param name="buttonPin">The GPIO pin number for the button input.</param>
    /// <param name="ledPin">The GPIO pin number for the LED output.</param>
    /// <param name="isPullUp">Whether to use pull-up (true) or pull-down (false) resistor configuration.</param>
    /// <param name="hasExternalResistor">Whether an external pull resistor is present.</param>
    /// <param name="gpioController">Optional GPIO controller instance. If null, a new instance will be created.</param>
    /// <param name="debounceTime">Time to wait between button state changes to filter out bounce.</param>
    public GpioButtonWithLed(int buttonPin, int ledPin, bool isPullUp = true, bool hasExternalResistor = false,
        GpioController? gpioController = null, TimeSpan debounceTime = default)
        : this(buttonPin, ledPin, TimeSpan.FromTicks(DefaultDoublePressTicks), TimeSpan.FromMilliseconds(DefaultHoldingMilliseconds), isPullUp, hasExternalResistor, new Implementation.GpioControllerWrapper(gpioController), debounceTime)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpioButtonWithLed"/> class with custom timing parameters.
    /// </summary>
    /// <param name="buttonPin">The GPIO pin number for the button input.</param>
    /// <param name="ledPin">The GPIO pin number for the LED output.</param>
    /// <param name="doublePress">Maximum time between button presses to register as a double press.</param>
    /// <param name="holding">Time to hold button before registering as a long press.</param>
    /// <param name="isPullUp">Whether to use pull-up (true) or pull-down (false) resistor configuration.</param>
    /// <param name="hasExternalResistor">Whether an external pull resistor is present.</param>
    /// <param name="gpioController">Optional GPIO controller instance. If null, a new instance will be created.</param>
    /// <param name="debounceTime">Time to wait between button state changes to filter out bounce.</param>
    /// <exception cref="ArgumentException">Thrown when button and LED pins are the same or pins don't support required modes.</exception>
    public GpioButtonWithLed(int buttonPin,
        int ledPin,
        TimeSpan doublePress,
        TimeSpan holding,
        bool isPullUp = true,
        bool hasExternalResistor = false,
        IGpioController? gpioController = null,
        TimeSpan debounceTime = default)
        : base(doublePress, holding, debounceTime)
    {
        if (buttonPin == ledPin)
        {
            throw new ArgumentException("Button pin and LED pin cannot be the same", nameof(ledPin));
        }

        _gpioController = gpioController ?? new GpioControllerWrapper();
        _shouldDispose = gpioController == null;
        _buttonPin = buttonPin;
        _ledPin = ledPin;
        HasExternalResistor = hasExternalResistor;
        DebounceTime = debounceTime; // Initialize the DebounceTime property

        _eventPinMode = isPullUp ? PinMode.InputPullUp : PinMode.InputPullDown;
        _gpioPinMode = hasExternalResistor ? PinMode.Input : _eventPinMode;

        ValidateAndInitializePins();
    }

    /// <summary>
    /// Gets a value indicating whether the circuit uses an external pull resistor.
    /// </summary>
    public bool HasExternalResistor { get; private set; } = false;

    /// <summary>
    /// Gets the debounce time used to filter out mechanical button bounce.
    /// Events occurring within this timeframe are ignored to prevent false triggers.
    /// </summary>
    public TimeSpan DebounceTime { get; private set; }

    /// <summary>
    /// Gets or sets the current LED state with thread-safe access.
    /// </summary>
    public PushButtonLedState LedState
    {
        get
        {
            // Fast path: check disposal without locking
            if (_disposed) return PushButtonLedState.Off;
            
            lock (_lockObject)
            {
                return _disposed ? PushButtonLedState.Off : _ledState;
            }
        }
        set
        {
            // Fast path: check disposal without locking
            if (_disposed) return;
            
            lock (_lockObject)
            {
                if (!_disposed && _ledState != value)
                {
                    _ledState = value;
                    UpdateLedHardware();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the LED behavior options.
    /// Updates LED hardware immediately when changed.
    /// </summary>
    public PushButtonLedOptions LedOptions 
    { 
        get => _ledOptions;
        set 
        {
            if (_disposed) return;
            
            lock (_lockObject)
            {
                if (!_disposed && _ledOptions != value)
                {
                    _ledOptions = value;
                    // Update LED immediately when options change
                    UpdateLedHardwareFromOptions();
                }
            }
        }
    }

    /// <summary>
    /// Validates GPIO pin capabilities and initializes the hardware configuration.
    /// Sets up both button and LED pins with proper error handling and cleanup.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when pins don't support required modes.</exception>
    /// <exception cref="InvalidOperationException">Thrown when GPIO initialization fails.</exception>
    private void ValidateAndInitializePins()
    {
        // Validate button pin support
        if (!_gpioController.IsPinModeSupported(_buttonPin, _gpioPinMode))
        {
            var errorMessage = _gpioPinMode == PinMode.Input
                ? $"GPIO pin {_buttonPin} cannot be configured as Input"
                : $"GPIO pin {_buttonPin} cannot be configured as {(_eventPinMode == PinMode.InputPullUp ? "pull-up" : "pull-down")}. Use an external resistor and set {nameof(HasExternalResistor)}=true";
            
            throw new ArgumentException(errorMessage, nameof(_buttonPin));
        }

        // Validate LED pin support
        if (!_gpioController.IsPinModeSupported(_ledPin, PinMode.Output))
        {
            throw new ArgumentException($"GPIO pin {_ledPin} cannot be configured as Output for LED", nameof(_ledPin));
        }

        // Track initialization state for proper cleanup on failure
        var buttonPinOpened = false;
        var ledPinOpened = false;
        var callbackRegistered = false;

        try
        {
            // Step 1: Open button pin
            try
            {
                _gpioController.OpenPin(_buttonPin, _gpioPinMode);
                buttonPinOpened = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to open button pin {_buttonPin} in mode {_gpioPinMode}", ex);
            }

            // Step 2: Open LED pin
            try
            {
                _gpioController.OpenPin(_ledPin, PinMode.Output);
                ledPinOpened = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to open LED pin {_ledPin} in output mode", ex);
            }

            // Step 3: Read initial button state before registering callback
            try
            {
                var initialValue = _gpioController.Read(_buttonPin);
                lock (_lockObject)
                {
                    // Set initial pressed state based on pin mode and current value
                    _isPressed = _eventPinMode == PinMode.InputPullUp ? 
                        initialValue == PinValue.Low : 
                        initialValue == PinValue.High;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to read initial button state from pin {_buttonPin}", ex);
            }

            // Step 4: Register button callback
            try
            {
                _gpioController.RegisterCallbackForPinValueChangedEvent(
                    _buttonPin,
                    PinEventTypes.Falling | PinEventTypes.Rising,
                    PinStateChanged);
                callbackRegistered = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to register callback for button pin {_buttonPin}", ex);
            }

            // Step 5: Initialize LED state (now with correct button state)
            try
            {
                UpdateLedHardwareFromOptions();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to initialize LED state on pin {_ledPin}", ex);
            }
        }
        catch (Exception)
        {
            // Clean up on failure to prevent resource leaks
            CleanupResources(callbackRegistered, ledPinOpened, buttonPinOpened);
            throw;
        }
    }

    /// <summary>
    /// Updates the LED hardware state based on current configuration.
    /// Includes error handling for disposal scenarios.
    /// </summary>
    private void UpdateLedHardware()
    {
        // Fast path: avoid GPIO operations if disposed
        if (_disposed) return;

        var pinValue = _ledState == PushButtonLedState.On ? PinValue.High : PinValue.Low;
        
        try
        {
            _gpioController.Write(_ledPin, pinValue);
        }
        catch (Exception ex)
        {
            // Only ignore GPIO errors during disposal
            if (!_disposed)
            {
                throw new InvalidOperationException(
                    $"Failed to update LED state on pin {_ledPin}", ex);
            }
        }
    }

    /// <summary>
    /// Updates the LED hardware state based on automatic behavior options.
    /// This is used when button events occur to update the LED according to the configured options.
    /// </summary>
    private void UpdateLedHardwareFromOptions()
    {
        // Fast path: avoid GPIO operations if disposed
        if (_disposed) return;

        var shouldTurnOn = ShouldLedBeOn();
        var newLedState = shouldTurnOn ? PushButtonLedState.On : PushButtonLedState.Off;
        
        // Update the LED state field and hardware
        _ledState = newLedState;
        
        var pinValue = shouldTurnOn ? PinValue.High : PinValue.Low;
        
        try
        {
            _gpioController.Write(_ledPin, pinValue);
        }
        catch (Exception ex)
        {
            // Only ignore GPIO errors during disposal
            if (!_disposed)
            {
                throw new InvalidOperationException(
                    $"Failed to update LED state on pin {_ledPin}", ex);
            }
        }
    }

    /// <summary>
    /// Determines whether the LED should be on based on current options and button state.
    /// This is used for automatic LED behavior controlled by LedOptions.
    /// </summary>
    /// <returns>True if the LED should be on, false otherwise.</returns>
    private bool ShouldLedBeOn()
    {
        return _ledOptions switch
        {
            PushButtonLedOptions.AlwaysOn => true,
            PushButtonLedOptions.AlwaysOff => false,
            PushButtonLedOptions.FollowPressedState => _isPressed,
            _ => false
        };
    }

    /// <summary>
    /// Handles GPIO pin state change events with thread-safe state management.
    /// Implements optimized locking and event handling patterns.
    /// </summary>
    /// <param name="sender">The source of the event (GPIO controller).</param>
    /// <param name="pinValueChangedEventArgs">Event arguments containing pin change details.</param>
    private void PinStateChanged(object sender, PinValueChangedEventArgs pinValueChangedEventArgs)
    {
        // Fast path: early exit if disposed
        if (_disposed) return;

        try
        {
            bool isPressed;
            bool callPressed;

            // Determine press state based on pin mode and event type
            if (_eventPinMode == PinMode.InputPullUp)
            {
                isPressed = pinValueChangedEventArgs.ChangeType == PinEventTypes.Falling;
                callPressed = isPressed;
            }
            else
            {
                isPressed = pinValueChangedEventArgs.ChangeType == PinEventTypes.Rising;
                callPressed = isPressed;
            }

            // Thread-safe state update
            lock (_lockObject)
            {
                if (!_disposed)
                {
                    _isPressed = isPressed;
                    
                    // Always update LED hardware - UpdateLedHardwareFromOptions() handles the logic
                    // This ensures LED stays consistent based on LedOptions setting
                    UpdateLedHardwareFromOptions();
                }
            }

            // Call button handlers outside lock to avoid deadlock
            if (!_disposed)
            {
                try
                {
                    if (callPressed)
                    {
                        // Call the base class method for button pressed
                        HandleButtonPressed();
                    }
                    else
                    {
                        // Call the base class method for button released
                        HandleButtonReleased();
                    }
                }
                catch (Exception ex)
                {
                    // Don't let button handler exceptions crash the GPIO monitoring
                    System.Diagnostics.Debug.WriteLine($"Error in button handler: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            // Don't let pin state change exceptions crash the application
            System.Diagnostics.Debug.WriteLine($"Error in PinStateChanged: {ex}");
        }
    }

    /// <summary>
    /// Optimized resource cleanup method to reduce code duplication and improve performance.
    /// Handles cleanup in the correct order to prevent resource leaks.
    /// </summary>
    /// <param name="callbackRegistered">Whether the pin callback was registered.</param>
    /// <param name="ledPinOpened">Whether the LED pin was opened.</param>
    /// <param name="buttonPinOpened">Whether the button pin was opened.</param>
    private void CleanupResources(bool callbackRegistered = true, bool ledPinOpened = true, bool buttonPinOpened = true)
    {
        var errors = new List<Exception>();

        // Step 1: Turn off LED first to prevent it staying on
        if (ledPinOpened)
        {
            try
            {
                _gpioController.Write(_ledPin, PinValue.Low);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        // Step 2: Unregister callback to prevent further events
        if (callbackRegistered)
        {
            try
            {
                _gpioController.UnregisterCallbackForPinValueChangedEvent(_buttonPin, PinStateChanged);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        // Step 3: Clean up GPIO resources
        if (_shouldDispose)
        {
            try
            {
                _gpioController?.Dispose();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }
        else
        {
            // Close individual pins if not disposing the entire controller
            if (buttonPinOpened)
            {
                try
                {
                    _gpioController.ClosePin(_buttonPin);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
            
            if (ledPinOpened)
            {
                try
                {
                    _gpioController.ClosePin(_ledPin);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        }

        // Report errors during cleanup (for debugging)
        if (errors.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"Cleanup completed with {errors.Count} errors");
        }
    }

    /// <summary>
    /// Optimized synchronous disposal implementation.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Set disposal flag first to prevent new operations
            _disposed = true;
            
            try
            {
                // Cleanup resources without additional locking
                CleanupResources();
            }
            catch (Exception ex)
            {
                // Don't let cleanup errors prevent disposal completion
                System.Diagnostics.Debug.WriteLine($"Error during disposal: {ex}");
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// High-performance asynchronous disposal implementation.
    /// Provides a small settling delay before cleanup for debounced operations.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        // Set disposal flag first to prevent new operations
        _disposed = true;

        try
        {
            // Allow brief settling time for any pending button operations
            if (DebounceTime > TimeSpan.Zero)
            {
                var settlingDelay = Math.Min(50, (int)DebounceTime.TotalMilliseconds);
                await Task.Delay(settlingDelay).ConfigureAwait(false);
            }

            // Cleanup resources
            CleanupResources();
        }
        catch (Exception ex)
        {
            // Don't let cleanup errors prevent disposal completion
            System.Diagnostics.Debug.WriteLine($"Error during async disposal: {ex}");
        }
        finally
        {
            // Call base disposal
            Dispose(true);
            
            // Suppress finalization
            GC.SuppressFinalize(this);
        }
    }
}
