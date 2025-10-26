using System.Device.Gpio;
using Iot.Device.Button;
using Microsoft.Extensions.Logging;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;

namespace HVO.Iot.Devices;

/// <summary>
/// Represents a GPIO button with integrated LED functionality, supporting both synchronous and asynchronous disposal patterns.
/// Provides thread-safe operation with optimized performance for high-frequency button events.
/// </summary>
public class GpioButtonWithLed : GpioButtonBase, IAsyncDisposable
{
    private readonly IGpioControllerClient _gpioController;
    private readonly PinMode _gpioPinMode;
    private readonly PinMode _eventPinMode;
    private readonly int _buttonPin;
    private readonly int? _ledPin;
    private readonly bool _shouldDispose;
    private readonly object _lockObject = new object();

    /// <summary>
    /// Logger instance for recording button and LED events and diagnostics.
    /// </summary>
    private readonly ILogger<GpioButtonWithLed>? _logger;

    /// <summary>
    /// Thread-safe disposal flag to prevent multiple disposal attempts.
    /// Uses volatile to ensure visibility across threads without locking.
    /// </summary>
    private volatile bool _disposed = false;

    /// <summary>
    /// Current LED state. Access is synchronized through _lockObject.
    /// </summary>
    private PushButtonLedState _ledState = PushButtonLedState.NotUsed;

    /// <summary>
    /// Current button press state. Access is synchronized through _lockObject.
    /// </summary>
    private bool _isPressed = false;

    /// <summary>
    /// Current LED options configuration. Access is synchronized through _lockObject.
    /// </summary>
    private PushButtonLedOptions _ledOptions = PushButtonLedOptions.FollowPressedState;

    // Blinking functionality fields
    /// <summary>
    /// Timer used for LED blinking operations. Access is synchronized through _lockObject.
    /// </summary>
    private System.Timers.Timer? _blinkTimer;

    /// <summary>
    /// Current blinking frequency in Hz. Valid range: 0.1Hz to 10Hz.
    /// </summary>
    private double _blinkFrequencyHz = 1.0;

    /// <summary>
    /// Internal state tracking for blink toggle. Access is synchronized through _lockObject.
    /// </summary>
    private bool _blinkToggleState = false;

    /// <summary>
    /// Flag indicating if blinking is currently active. Access is synchronized through _lockObject.
    /// </summary>
    private bool _isBlinking = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpioButtonWithLed"/> class with default timing parameters.
    /// </summary>
    /// <param name="buttonPin">The GPIO pin number for the button input.</param>
    /// <param name="ledPin">The GPIO pin number for the LED output.</param>
    /// <param name="isPullUp">Whether to use pull-up (true) or pull-down (false) resistor configuration.</param>
    /// <param name="hasExternalResistor">Whether an external pull resistor is present.</param>
    /// <param name="debounceTime">Time to wait between button state changes to filter out bounce.</param>
    /// <param name="logger">Optional logger for recording events and diagnostics.</param>
    public GpioButtonWithLed(int buttonPin, int? ledPin, bool isPullUp = true, bool hasExternalResistor = false,
        TimeSpan debounceTime = default, ILogger<GpioButtonWithLed>? logger = null)
    : this(buttonPin, ledPin, TimeSpan.FromTicks(DefaultDoublePressTicks), TimeSpan.FromMilliseconds(DefaultHoldingMilliseconds), isPullUp, hasExternalResistor, Implementation.GpioControllerClientFactory.CreateAutoSelecting(), debounceTime, logger)
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
    /// <param name="logger">Optional logger for recording events and diagnostics.</param>
    /// <exception cref="ArgumentException">Thrown when button and LED pins are the same or pins don't support required modes.</exception>
    public GpioButtonWithLed(int buttonPin,
        int? ledPin,
        TimeSpan doublePress,
        TimeSpan holding,
        bool isPullUp = true,
        bool hasExternalResistor = false,
    IGpioControllerClient? gpioController = null,
        TimeSpan debounceTime = default,
        ILogger<GpioButtonWithLed>? logger = null)
        : base(doublePress, holding, debounceTime)
    {
        if (ledPin.HasValue && buttonPin == ledPin.Value)
        {
            throw new ArgumentException("Button pin and LED pin cannot be the same", nameof(ledPin));
        }

    _gpioController = gpioController ?? GpioControllerClientFactory.CreateAutoSelecting();
        _shouldDispose = gpioController == null;
        _buttonPin = buttonPin;
        _ledPin = ledPin;
        _logger = logger;
        HasExternalResistor = hasExternalResistor;

        _eventPinMode = isPullUp ? PinMode.InputPullUp : PinMode.InputPullDown;
        _gpioPinMode = hasExternalResistor ? PinMode.Input : _eventPinMode;

        // Initialize LED state based on whether LED pin is configured
        _ledState = _ledPin.HasValue ? PushButtonLedState.Off : PushButtonLedState.NotUsed;

        ValidateAndInitializePins();
    }

    /// <summary>
    /// Gets a value indicating whether the circuit uses an external pull resistor.
    /// </summary>
    public bool HasExternalResistor { get; private set; } = false;

    /// <summary>
    /// Gets or sets the current LED state with thread-safe access.
    /// Setting a state other than NotUsed when no LED pin is configured will be ignored.
    /// </summary>
    public PushButtonLedState LedState
    {
        get
        {
            // Fast path: check disposal without locking
            if (_disposed) return _ledPin.HasValue ? PushButtonLedState.Off : PushButtonLedState.NotUsed;

            lock (_lockObject)
            {
                return _disposed ? (_ledPin.HasValue ? PushButtonLedState.Off : PushButtonLedState.NotUsed) : _ledState;
            }
        }
        set
        {
            // Fast path: check disposal without locking
            if (_disposed) return;

            // Cannot set LED state when no LED pin is configured
            if (!_ledPin.HasValue && value != PushButtonLedState.NotUsed) return;

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
    /// Updates LED hardware immediately when changed (only if LED pin is configured).
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
                    var previousOptions = _ledOptions;
                    _ledOptions = value;

                    _logger?.LogDebug("LED Options Changed - From: {PreviousOptions} to: {NewOptions}, LED Pin: {LedPin}, Blinking: {IsBlinking}",
                        previousOptions, value, _ledPin?.ToString() ?? "None", _isBlinking);

                    // Update LED immediately when options change (only if LED pin is configured)
                    if (_ledPin.HasValue)
                    {
                        UpdateLedHardwareFromOptions();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the LED is currently in blinking mode.
    /// </summary>
    public bool IsBlinking
    {
        get
        {
            if (_disposed) return false;

            lock (_lockObject)
            {
                return !_disposed && _isBlinking;
            }
        }
    }

    /// <summary>
    /// Starts LED blinking at the specified frequency.
    /// </summary>
    /// <param name="frequencyHz">Blinking frequency in Hz. Valid range: 0.1Hz to 10Hz.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when frequency is outside the valid range.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no LED pin is configured or object is disposed.</exception>
    public void StartBlinking(double frequencyHz)
    {
        if (_disposed)
            throw new InvalidOperationException("Cannot start blinking on disposed object");

        if (!_ledPin.HasValue)
            throw new InvalidOperationException("Cannot start blinking when no LED pin is configured");

        if (frequencyHz < 0.1 || frequencyHz > 10.0)
            throw new ArgumentOutOfRangeException(nameof(frequencyHz), frequencyHz,
                "Blinking frequency must be between 0.1Hz and 10Hz");

        lock (_lockObject)
        {
            if (_disposed) return;

            _logger?.LogDebug("Starting Blink - Pin: {LedPin}, Frequency: {FrequencyHz}Hz, Options: {LedOptions}",
                _ledPin.Value, frequencyHz, _ledOptions);

            // Stop any existing blinking
            StopBlinkingInternal();

            // Set up new blinking parameters
            _blinkFrequencyHz = frequencyHz;
            _blinkToggleState = false;
            _isBlinking = true;
            _ledState = PushButtonLedState.Blinking;

            // Create and start the blink timer
            var intervalMs = 500.0 / frequencyHz; // Half period for toggle
            _blinkTimer = new System.Timers.Timer(intervalMs);
            _blinkTimer.Elapsed += OnBlinkTimerElapsed;
            _blinkTimer.AutoReset = true;
            _blinkTimer.Start();

            _logger?.LogDebug("Blink Started - Interval: {IntervalMs}ms, Timer Active: {TimerEnabled}",
                intervalMs, _blinkTimer.Enabled);

            // Start with LED on
            UpdateBlinkHardware();
        }
    }

    /// <summary>
    /// Stops LED blinking and sets the LED to the off state.
    /// </summary>
    public void StopBlinking()
    {
        if (_disposed) return;

        lock (_lockObject)
        {
            if (!_disposed)
            {
                _logger?.LogDebug("Stopping Blink - Pin: {LedPin}, Was Blinking: {WasBlinking}",
                    _ledPin?.ToString() ?? "None", _isBlinking);

                StopBlinkingInternal();
                // Update LED state based on current options
                UpdateLedHardwareFromOptions();

                _logger?.LogDebug("Blink Stopped - LED state restored based on options: {LedOptions}",
                    _ledOptions);
            }
        }
    }

    /// <summary>
    /// Changes the blinking frequency while blinking is active.
    /// If blinking is not active, this method updates the frequency for the next time blinking starts.
    /// </summary>
    /// <param name="frequencyHz">New blinking frequency in Hz. Valid range: 0.1Hz to 10Hz.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when frequency is outside the valid range.</exception>
    public void SetBlinkFrequency(double frequencyHz)
    {
        if (frequencyHz < 0.1 || frequencyHz > 10.0)
            throw new ArgumentOutOfRangeException(nameof(frequencyHz), frequencyHz,
                "Blinking frequency must be between 0.1Hz and 10Hz");

        if (_disposed) return;

        lock (_lockObject)
        {
            if (_disposed) return;

            var oldFrequency = _blinkFrequencyHz;
            _blinkFrequencyHz = frequencyHz;

            _logger?.LogDebug("Frequency Change - From: {OldFrequency}Hz to {NewFrequency}Hz, Currently Blinking: {IsBlinking}",
                oldFrequency, frequencyHz, _isBlinking);

            // If currently blinking, restart with new frequency
            if (_isBlinking && _blinkTimer != null)
            {
                var intervalMs = 500.0 / frequencyHz; // Half period for toggle
                _blinkTimer.Stop();
                _blinkTimer.Interval = intervalMs;
                _blinkTimer.Start();

                _logger?.LogDebug("Timer Updated - New Interval: {IntervalMs}ms, Timer Active: {TimerEnabled}",
                    intervalMs, _blinkTimer.Enabled);
            }
        }
    }

    /// <summary>
    /// Internal method to stop blinking without updating LED state from options.
    /// Must be called within a lock on _lockObject.
    /// </summary>
    private void StopBlinkingInternal()
    {
        if (_blinkTimer != null)
        {
            _blinkTimer.Stop();
            _blinkTimer.Elapsed -= OnBlinkTimerElapsed;
            _blinkTimer.Dispose();
            _blinkTimer = null;
        }

        _isBlinking = false;
        _blinkToggleState = false;

        // Set LED state back to Off (will be updated by caller as needed)
        if (_ledState == PushButtonLedState.Blinking)
        {
            _ledState = PushButtonLedState.Off;
        }
    }

    /// <summary>
    /// Timer elapsed event handler for LED blinking.
    /// Thread-safe with proper disposal checking.
    /// </summary>
    /// <param name="sender">The timer that elapsed.</param>
    /// <param name="e">Event arguments.</param>
    private void OnBlinkTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_disposed) return;

        try
        {
            lock (_lockObject)
            {
                if (_disposed || !_isBlinking) return;

                // Toggle the blink state
                var previousState = _blinkToggleState;
                _blinkToggleState = !_blinkToggleState;

                _logger?.LogTrace("Blink Toggle - From: {PreviousState} to: {NewState}, Frequency: {FrequencyHz}Hz",
                    previousState, _blinkToggleState, _blinkFrequencyHz);

                // Update hardware based on current conditions
                UpdateBlinkHardware();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Blink Timer Error - {ErrorMessage}", ex.Message);

            // Don't let timer exceptions crash the application
        }
    }

    /// <summary>
    /// Updates the LED hardware during blinking operations.
    /// Handles integration with LED options for proper behavior.
    /// Must be called within a lock on _lockObject.
    /// </summary>
    private void UpdateBlinkHardware()
    {
        if (_disposed || !_ledPin.HasValue || !_isBlinking) return;

        bool shouldBeOn;
        string reason;

        // Determine LED state based on options and blinking state
        switch (_ledOptions)
        {
            case PushButtonLedOptions.AlwaysOff:
                // When AlwaysOff, no LED or blinking should occur
                shouldBeOn = false;
                reason = "AlwaysOff option overrides blinking";
                break;

            case PushButtonLedOptions.AlwaysOn:
                // When AlwaysOn, LED stays on unless in blinking state where it follows blink pattern
                shouldBeOn = _blinkToggleState;
                reason = $"AlwaysOn with blinking - toggle state: {_blinkToggleState}";
                break;

            case PushButtonLedOptions.FollowPressedState:
                // When following pressed state:
                // - If button is pressed, LED is on (overrides blinking)
                // - If button is not pressed, follow blink pattern
                shouldBeOn = _isPressed || _blinkToggleState;
                reason = _isPressed
                    ? "Button pressed overrides blinking"
                    : $"Following blink pattern - toggle state: {_blinkToggleState}";
                break;

            default:
                shouldBeOn = _blinkToggleState;
                reason = $"Default blinking behavior - toggle state: {_blinkToggleState}";
                break;
        }

        var pinValue = shouldBeOn ? PinValue.High : PinValue.Low;

        _logger?.LogTrace("Blink Update - Pin: {LedPin}, Value: {PinValue}, Frequency: {FrequencyHz}Hz, Options: {LedOptions}, Reason: {Reason}",
            _ledPin.Value, pinValue, _blinkFrequencyHz, _ledOptions, reason);

        try
        {
            _gpioController.Write(_ledPin.Value, pinValue);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Blink Update Failed - Pin: {LedPin}, Error: {ErrorMessage}",
                _ledPin.Value, ex.Message);

            // Only ignore GPIO errors during disposal
            if (!_disposed)
            {
                _logger?.LogError(ex, "Failed to update LED during blinking");
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

        // Validate LED pin support (only if LED pin is configured)
        if (_ledPin.HasValue && !_gpioController.IsPinModeSupported(_ledPin.Value, PinMode.Output))
        {
            throw new ArgumentException($"GPIO pin {_ledPin.Value} cannot be configured as Output for LED", nameof(_ledPin));
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
                _gpioController.OpenPin(_buttonPin, _gpioPinMode, initialValue: PinValue.High);
                buttonPinOpened = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to open button pin {_buttonPin} in mode {_gpioPinMode}", ex);
            }

            // Step 2: Open LED pin (only if configured)
            if (_ledPin.HasValue)
            {
                try
                {
                    _gpioController.OpenPin(_ledPin.Value, PinMode.Output, initialValue: PinValue.High);
                    ledPinOpened = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to open LED pin {_ledPin.Value} in output mode", ex);
                }
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
    /// Includes error handling for disposal scenarios and null LED pins.
    /// </summary>
    private void UpdateLedHardware()
    {
        // Fast path: avoid GPIO operations if disposed or no LED pin configured
        if (_disposed || !_ledPin.HasValue) return;

        // Don't interfere with blinking - let the blink timer handle LED updates
        if (_ledState == PushButtonLedState.Blinking && _isBlinking) return;

        var pinValue = _ledState == PushButtonLedState.On ? PinValue.High : PinValue.Low;
        var reason = "Direct LED state change";

        _logger?.LogDebug("LED Update - Pin: {LedPin}, State: {LedState}, Value: {PinValue}, Reason: {Reason}",
            _ledPin.Value, _ledState, pinValue, reason);

        try
        {
            _gpioController.Write(_ledPin.Value, pinValue);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "LED Update Failed - Pin: {LedPin}, Error: {ErrorMessage}",
                _ledPin.Value, ex.Message);

            // Only ignore GPIO errors during disposal
            if (!_disposed)
            {
                throw new InvalidOperationException(
                    $"Failed to update LED state on pin {_ledPin.Value}", ex);
            }
        }
    }

    /// <summary>
    /// Updates the LED hardware state based on automatic behavior options.
    /// This is used when button events occur to update the LED according to the configured options.
    /// </summary>
    private void UpdateLedHardwareFromOptions()
    {
        // Fast path: avoid GPIO operations if disposed or no LED pin configured
        if (_disposed || !_ledPin.HasValue) return;

        // If currently blinking, let the blink timer handle LED updates
        // except when FollowPressedState and button is pressed (which overrides blinking)
        if (_isBlinking && !(_ledOptions == PushButtonLedOptions.FollowPressedState && _isPressed))
        {
            _logger?.LogTrace("LED Update Skipped - Blinking active, Options: {LedOptions}, ButtonPressed: {ButtonPressed}",
                _ledOptions, _isPressed);
            return;
        }

        var shouldTurnOn = ShouldLedBeOn();
        var newLedState = shouldTurnOn ? PushButtonLedState.On : PushButtonLedState.Off;
        var reason = GetLedChangeReason();

        // Only update if not currently blinking or if we need to override for pressed state
        if (!_isBlinking || (_ledOptions == PushButtonLedOptions.FollowPressedState && _isPressed))
        {
            _ledState = newLedState;

            var pinValue = shouldTurnOn ? PinValue.High : PinValue.Low;

            _logger?.LogDebug("LED Update from Options - Pin: {LedPin}, State: {NewLedState}, Value: {PinValue}, Options: {LedOptions}, ButtonPressed: {ButtonPressed}, Reason: {Reason}",
                _ledPin.Value, newLedState, pinValue, _ledOptions, _isPressed, reason);

            try
            {
                _gpioController.Write(_ledPin.Value, pinValue);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "LED Update Failed - Pin: {LedPin}, Error: {ErrorMessage}",
                    _ledPin.Value, ex.Message);

                // Only ignore GPIO errors during disposal
                if (!_disposed)
                {
                    throw new InvalidOperationException(
                        $"Failed to update LED state on pin {_ledPin.Value}", ex);
                }
            }
        }
        else
        {
            _logger?.LogTrace("LED Update Blocked by Blinking - Current blinking state overrides options");
        }
    }

    /// <summary>
    /// Determines whether the LED should be on based on current options and button state.
    /// This is used for automatic LED behavior controlled by LedOptions.
    /// Returns false if no LED pin is configured.
    /// </summary>
    /// <returns>True if the LED should be on, false otherwise.</returns>
    private bool ShouldLedBeOn()
    {
        // If no LED pin is configured, LED should never be on
        if (!_ledPin.HasValue) return false;

        return _ledOptions switch
        {
            PushButtonLedOptions.AlwaysOn => true,
            PushButtonLedOptions.AlwaysOff => false,
            PushButtonLedOptions.FollowPressedState => _isPressed,
            _ => false
        };
    }

    /// <summary>
    /// Gets a descriptive reason for the current LED state change based on options and button state.
    /// Used for debug logging to help troubleshoot LED behavior.
    /// </summary>
    /// <returns>A string describing why the LED state is changing.</returns>
    private string GetLedChangeReason()
    {
        return _ledOptions switch
        {
            PushButtonLedOptions.AlwaysOn => "AlwaysOn option - LED forced on",
            PushButtonLedOptions.AlwaysOff => "AlwaysOff option - LED forced off",
            PushButtonLedOptions.FollowPressedState when _isPressed => "Button pressed - LED follows pressed state",
            PushButtonLedOptions.FollowPressedState when !_isPressed => "Button released - LED follows pressed state",
            _ => "Unknown LED option"
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

            _logger?.LogDebug("Button Event - Pin: {ButtonPin}, Event: {ChangeType}, Pressed: {IsPressed}, PinMode: {EventPinMode}",
                _buttonPin, pinValueChangedEventArgs.ChangeType, isPressed, _eventPinMode);

            // Thread-safe state update
            lock (_lockObject)
            {
                if (!_disposed)
                {
                    var previousPressed = _isPressed;
                    _isPressed = isPressed;

                    _logger?.LogDebug("Button State Change - From: {PreviousPressed} to: {NewPressed}, LED Options: {LedOptions}, Blinking: {IsBlinking}",
                        previousPressed, _isPressed, _ledOptions, _isBlinking);

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
                        _logger?.LogDebug("Button Pressed Event - Calling base handler");

                        // Call the base class method for button pressed
                        HandleButtonPressed();
                    }
                    else
                    {
                        _logger?.LogDebug("Button Released Event - Calling base handler");

                        // Call the base class method for button released
                        HandleButtonReleased();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Button Handler Error - {ErrorMessage}", ex.Message);

                    // Don't let button handler exceptions crash the GPIO monitoring
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Pin State Change Error - {ErrorMessage}", ex.Message);

            // Don't let pin state change exceptions crash the application
        }
    }

    /// <summary>
    /// Handles changes in GPIO pin, based on whether the system is pullup or pulldown.
    /// </summary>
    /// <param name="sender">The sender object.</param>
    /// <param name="pinValueChangedEventArgs">The pin argument changes.</param>
    private void XX_PinStateChanged(object sender, PinValueChangedEventArgs pinValueChangedEventArgs)
    {
        // Fast path: early exit if disposed
        if (_disposed) return;

        switch (pinValueChangedEventArgs.ChangeType)
        {
            case PinEventTypes.Falling:
                if (_eventPinMode == PinMode.InputPullUp)
                {
                    HandleButtonPressed();
                }
                else
                {
                    HandleButtonReleased();
                }

                break;
            case PinEventTypes.Rising:
                if (_eventPinMode == PinMode.InputPullUp)
                {
                    HandleButtonReleased();
                }
                else
                {
                    HandleButtonPressed();
                }

                break;
        }
    }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    protected override void HandleButtonPressed()
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    {
        base.HandleButtonPressed();
    }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    protected override void HandleButtonReleased()
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    {
        base.HandleButtonReleased();
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

        _logger?.LogDebug("Starting Cleanup - Button Pin: {ButtonPin}, LED Pin: {LedPin}, Blinking: {IsBlinking}",
            _buttonPin, _ledPin?.ToString() ?? "None", _isBlinking);

        // Step 1: Stop blinking timer first to prevent interference
        try
        {
            if (_isBlinking)
            {
                _logger?.LogDebug("Cleanup - Stopping blinking timer");
            }

            StopBlinkingInternal();
            _blinkTimer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Cleanup Error - Blinking timer: {ErrorMessage}", ex.Message);
            errors.Add(ex);
        }

        // Step 2: Turn off LED to prevent it staying on (only if LED pin is configured)
        if (ledPinOpened && _ledPin.HasValue)
        {
            try
            {
                _logger?.LogDebug("Cleanup - Turning off LED on pin {LedPin}", _ledPin.Value);

                _gpioController.WriteLow(_ledPin.Value);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Cleanup Error - LED off: {ErrorMessage}", ex.Message);
                errors.Add(ex);
            }
        }

        // Step 3: Unregister callback to prevent further events
        if (callbackRegistered)
        {
            try
            {
                _logger?.LogDebug("Cleanup - Unregistering callback for pin {ButtonPin}", _buttonPin);

                //_gpioController.UnregisterCallbackForPinValueChangedEvent(_buttonPin, PinStateChanged);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Cleanup Error - Callback unregister: {ErrorMessage}", ex.Message);
                errors.Add(ex);
            }
        }

        // Step 4: Clean up GPIO resources
        if (_shouldDispose)
        {
            try
            {
                _logger?.LogDebug("Cleanup - Disposing GPIO controller");

                _gpioController?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Cleanup Error - GPIO controller dispose: {ErrorMessage}", ex.Message);
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
                    _logger?.LogDebug("Cleanup - Closing button pin {ButtonPin}", _buttonPin);

                    _gpioController.ClosePin(_buttonPin);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Cleanup Error - Button pin close: {ErrorMessage}", ex.Message);
                    errors.Add(ex);
                }
            }

            if (ledPinOpened && _ledPin.HasValue)
            {
                try
                {
                    _logger?.LogDebug("Cleanup - Closing LED pin {LedPin}", _ledPin.Value);

                    _gpioController.ClosePin(_ledPin.Value);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Cleanup Error - LED pin close: {ErrorMessage}", ex.Message);
                    errors.Add(ex);
                }
            }
        }

        // Report errors during cleanup (for debugging)
        if (errors.Count > 0)
        {
            _logger?.LogWarning("Cleanup completed with {ErrorCount} errors", errors.Count);
        }
        else
        {
            _logger?.LogDebug("Cleanup completed successfully");
        }
    }

    /// <summary>
    /// Optimized synchronous disposal implementation.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            _logger?.LogTrace("Dispose(disposing: {Disposing}) - Already disposed, returning", disposing);
            return;
        }

        _logger?.LogDebug("Dispose(disposing: {Disposing}) - Starting disposal for Button Pin: {ButtonPin}, LED Pin: {LedPin}",
            disposing, _buttonPin, _ledPin?.ToString() ?? "None");

        if (disposing)
        {
            // Set disposal flag first to prevent new operations
            _disposed = true;

            try
            {
                _logger?.LogDebug("Dispose - Starting cleanup of managed resources");

                // Cleanup resources without additional locking
                CleanupResources();

                _logger?.LogDebug("Dispose - Managed resource cleanup completed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Dispose Error - Cleanup failed: {ErrorMessage}", ex.Message);

                // Don't let cleanup errors prevent disposal completion
            }
        }

        _logger?.LogDebug("Dispose - Calling base.Dispose()");
        base.Dispose(disposing);
        _logger?.LogDebug("Dispose - Completed successfully");
    }

    /// <summary>
    /// High-performance asynchronous disposal implementation.
    /// Provides a small settling delay before cleanup for debounced operations.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            _logger?.LogTrace("DisposeAsync - Already disposed, returning");
            return;
        }

        _logger?.LogDebug("DisposeAsync - Starting async disposal for Button Pin: {ButtonPin}, LED Pin: {LedPin}",
            _buttonPin, _ledPin?.ToString() ?? "None");

        // Set disposal flag first to prevent new operations
        _disposed = true;

        try
        {
            // Allow brief settling time for any pending button operations
            if (DebounceTime > TimeSpan.Zero)
            {
                var settlingDelay = Math.Min(50, (int)DebounceTime.TotalMilliseconds);
                _logger?.LogDebug("DisposeAsync - Waiting {SettlingDelay}ms for settling", settlingDelay);

                await Task.Delay(settlingDelay).ConfigureAwait(false);
            }

            _logger?.LogDebug("DisposeAsync - Starting cleanup of resources");

            // Cleanup resources
            CleanupResources();

            _logger?.LogDebug("DisposeAsync - Resource cleanup completed");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DisposeAsync Error - Cleanup failed: {ErrorMessage}", ex.Message);

            // Don't let cleanup errors prevent disposal completion
        }
        finally
        {
            _logger?.LogDebug("DisposeAsync - Calling base disposal and suppressing finalization");

            // Call base disposal
            Dispose(true);

            // Suppress finalization
            GC.SuppressFinalize(this);

            _logger?.LogDebug("DisposeAsync - Completed successfully");
        }
    }
}


/// <summary>
/// 
/// </summary>
public abstract class GpioButtonBase : IDisposable
{
    /// <summary>
    /// 
    /// </summary>
    protected internal const long DefaultDoublePressTicks = 15000000;

    /// <summary>
    /// 
    /// </summary>
    protected internal const long DefaultHoldingMilliseconds = 2000;

    private bool _disposed = false;

    private long _doublePressTicks;
    private long _holdingMs;
    private long _debounceStartTicks;

    private ButtonHoldingState _holdingState = ButtonHoldingState.Completed;

    private long _lastPress = DateTime.MinValue.Ticks;
    private Timer? _holdingTimer;

    /// <summary>
    /// Delegate for button up event.
    /// </summary>
    public event EventHandler<EventArgs>? ButtonUp;

    /// <summary>
    /// Delegate for button down event.
    /// </summary>
    public event EventHandler<EventArgs>? ButtonDown;

    /// <summary>
    /// Delegate for button pressed event.
    /// </summary>
    public event EventHandler<EventArgs>? Press;

    /// <summary>
    /// Delegate for button double pressed event.
    /// </summary>
    public event EventHandler<EventArgs>? DoublePress;

    /// <summary>
    /// Delegate for button holding event.
    /// </summary>
    public event EventHandler<ButtonHoldingEventArgs>? Holding;

    /// <summary>
    /// Define if holding event is enabled or disabled on the button.
    /// </summary>
    public bool IsHoldingEnabled { get; set; } = false;

    /// <summary>
    /// Define if double press event is enabled or disabled on the button.
    /// </summary>
    public bool IsDoublePressEnabled { get; set; } = false;

    /// <summary>
    /// Indicates if the button is currently pressed.
    /// </summary>
    public bool IsPressed { get; set; } = false;

    /// <summary>
    /// Gets the debounce time used to filter out mechanical button bounce.
    /// Events occurring within this timeframe are ignored to prevent false triggers.
    /// </summary>
    public TimeSpan DebounceTime { get; private set; }

    /// <summary>
    /// Initialization of the button.
    /// </summary>
    public GpioButtonBase()
        : this(TimeSpan.FromTicks(DefaultDoublePressTicks), TimeSpan.FromMilliseconds(DefaultHoldingMilliseconds), default)
    {
    }

    /// <summary>
    /// Initialization of the button.
    /// </summary>
    /// <param name="doublePress">Max ticks between button presses to count as doublePress.</param>
    /// <param name="holding">Min ms a button is pressed to count as holding.</param>
    /// <param name="debounceTime">The amount of time during which the transitions are ignored, or zero</param>
    public GpioButtonBase(TimeSpan doublePress, TimeSpan holding, TimeSpan debounceTime)
    {
        if (debounceTime.TotalMilliseconds * 3 > doublePress.TotalMilliseconds)
        {
            throw new ArgumentException($"The parameter {nameof(doublePress)} should be at least three times {nameof(debounceTime)}");
        }

        _doublePressTicks = doublePress.Ticks;
        _holdingMs = (long)holding.TotalMilliseconds;
        DebounceTime = debounceTime;
    }

    /// <summary>
    /// Handler for pressing the button.
    /// </summary>
    protected virtual void HandleButtonPressed()
    {
        if (DateTime.UtcNow.Ticks - _debounceStartTicks < DebounceTime.Ticks)
        {
            return;
        }

        IsPressed = true;

        ButtonDown?.Invoke(this, new EventArgs());

        if (IsHoldingEnabled)
        {
            _holdingTimer = new Timer(StartHoldingHandler, null, (int)_holdingMs, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Handler for releasing the button.
    /// </summary>
    protected virtual void HandleButtonReleased()
    {
        if (DebounceTime.Ticks > 0 && !IsPressed)
        {
            return;
        }

        _debounceStartTicks = DateTime.UtcNow.Ticks;
        _holdingTimer?.Dispose();
        _holdingTimer = null;

        IsPressed = false;

        ButtonUp?.Invoke(this, new EventArgs());
        Press?.Invoke(this, new EventArgs());

        if (IsHoldingEnabled && _holdingState == ButtonHoldingState.Started)
        {
            _holdingState = ButtonHoldingState.Completed;
            Holding?.Invoke(this, new ButtonHoldingEventArgs { HoldingState = ButtonHoldingState.Completed });
        }

        if (IsDoublePressEnabled)
        {
            if (_lastPress == DateTime.MinValue.Ticks)
            {
                _lastPress = DateTime.UtcNow.Ticks;
            }
            else
            {
                if (DateTime.UtcNow.Ticks - _lastPress <= _doublePressTicks)
                {
                    DoublePress?.Invoke(this, new EventArgs());
                }

                _lastPress = DateTime.MinValue.Ticks;
            }
        }
    }

    /// <summary>
    /// Handler for holding the button.
    /// </summary>
    private void StartHoldingHandler(object? state)
    {
        _holdingTimer?.Dispose();
        _holdingTimer = null;
        _holdingState = ButtonHoldingState.Started;

        Holding?.Invoke(this, new ButtonHoldingEventArgs { HoldingState = ButtonHoldingState.Started });
    }

    /// <summary>
    /// Cleanup resources.
    /// </summary>
    /// <param name="disposing">Disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _holdingTimer?.Dispose();
            _holdingTimer = null;
        }

        _disposed = true;
    }

    /// <summary>
    /// Public dispose method for IDisposable interface.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
    }
}    
