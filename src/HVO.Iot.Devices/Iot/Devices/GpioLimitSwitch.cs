using System;
using System.Device.Gpio;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices;

/// <summary>
/// Represents a GPIO-based limit switch with support for internal/external pull resistors, 
/// debounce handling, and comprehensive logging. This class provides high-performance 
/// monitoring of GPIO pin state changes with configurable debouncing and caching.
/// </summary>
/// <remarks>
/// The limit switch supports both pull-up and pull-down configurations, with optional
/// external resistors. It uses a high-performance Stopwatch for precise timing and
/// implements efficient caching to minimize expensive GPIO operations.
/// </remarks>
public class GpioLimitSwitch : IAsyncDisposable, IDisposable
{
/// <summary>
    /// Tracks the state of initialization steps for proper cleanup on failure.
    /// </summary>
    private class InitializationState
    {
        public bool WasAlreadyOpen { get; set; }
        public bool PinOpened { get; set; }
        public bool InitialValueCached { get; set; }
        public bool CallbackRegistered { get; set; }
    }
        
    /// <summary>
    /// Logger instance for recording limit switch events and diagnostics.
    /// </summary>
    private readonly ILogger<GpioLimitSwitch>? _logger;
    
    /// <summary>
    /// Synchronization object for thread-safe operations on shared state.
    /// </summary>
    private readonly object _objLock = new();
    
    /// <summary>
    /// High-performance timer for accurate debounce timing calculations.
    /// Started immediately to provide consistent timing reference.
    /// </summary>
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    
    /// <summary>
    /// Cached pin value to avoid expensive GPIO read operations.
    /// Updated whenever pin state changes occur to maintain accuracy.
    /// Thread-safe access is ensured through proper locking mechanisms.
    /// </summary>
    private PinValue _lastPinValue; // Thread-safe access via locking
    
    /// <summary>
    /// Flag to track if the pin value has been initialized.
    /// This prevents confusion with PinValue.Low as the default value.
    /// </summary>
    private bool _pinValueInitialized = false;
    
    /// <summary>
    /// Thread-safe disposal flag to prevent multiple disposal attempts.
    /// Uses volatile to ensure visibility across threads without locking.
    /// </summary>
    private volatile bool _disposed = false;

    /// <summary>
    /// Records the timestamp of the last pin event for debounce logic.
    /// Uses Stopwatch ticks for high-precision timing comparisons.
    /// Updated atomically using Interlocked operations for thread safety.
    /// </summary>
    private long _lastEventTicks;
    
    /// <summary>
    /// Records the type of the last pin event for debounce logic.
    /// Used to prevent duplicate events of the same type.
    /// </summary>
    private PinEventTypes _lastEventType = PinEventTypes.None;

    /// <summary>
    /// Indicates whether the limit switch is in simulation mode.
    /// When true, the IsTriggered property uses the simulated state instead of the actual GPIO pin.
    /// </summary>
    private bool _simulationMode = false;
    
    /// <summary>
    /// The simulated triggered state when in simulation mode.
    /// This value is used by IsTriggered when _simulationMode is true.
    /// </summary>
    private bool _simulatedTriggeredState = false;

    /// <summary>
    /// Pre-calculated debounce threshold in Stopwatch ticks for optimal performance.
    /// Avoids repeated calculations during event processing.
    /// </summary>
    private readonly long _debounceTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpioLimitSwitch"/> class with comprehensive configuration options.
    /// This constructor automatically selects the appropriate GPIO controller based on environment.
    /// </summary>
    /// <param name="gpioPinNumber">The GPIO pin number to monitor. Must be greater than 0.</param>
    /// <param name="isPullup">
    /// If true, configures the pin with internal pull-up resistor (pin reads High when switch is open).
    /// If false, configures with pull-down resistor (pin reads Low when switch is open).
    /// </param>
    /// <param name="hasExternalResistor">
    /// If true, assumes external pull resistor is present and uses Input mode.
    /// If false, relies on internal pull resistor configuration.
    /// </param>
    /// <param name="debounceTime">
    /// Minimum time between valid pin state changes. Events occurring within this timeframe
    /// are filtered out to eliminate mechanical switch bounce. Default is no debouncing.
    /// </param>
    /// <param name="logger">Optional logger for recording events and diagnostics.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when gpioPinNumber is less than or equal to 0.</exception>
    /// <exception cref="ArgumentException">Thrown when the specified pin doesn't support the required mode.</exception>
    public GpioLimitSwitch(
        int gpioPinNumber,
        bool isPullup = true,
        bool hasExternalResistor = false,
        TimeSpan debounceTime = default,
        ILogger<GpioLimitSwitch>? logger = null)
        : this(Implementation.GpioControllerWrapper.CreateAutoSelecting(), gpioPinNumber, isPullup, hasExternalResistor, debounceTime, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpioLimitSwitch"/> class with comprehensive configuration options.
    /// </summary>
    /// <param name="gpioController">The GPIO controller to use for pin operations. Must not be null.</param>
    /// <param name="gpioPinNumber">The GPIO pin number to monitor. Must be greater than 0.</param>
    /// <param name="isPullup">
    /// If true, configures the pin with internal pull-up resistor (pin reads High when switch is open).
    /// If false, configures with pull-down resistor (pin reads Low when switch is open).
    /// </param>
    /// <param name="hasExternalResistor">
    /// If true, assumes external pull resistor is present and uses Input mode.
    /// If false, relies on internal pull resistor configuration.
    /// </param>
    /// <param name="debounceTime">
    /// Minimum time between valid pin state changes. Events occurring within this timeframe
    /// are filtered out to eliminate mechanical switch bounce. Default is no debouncing.
    /// </param>
    /// <param name="logger">Optional logger for recording events and diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when gpioController is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when gpioPinNumber is less than or equal to 0.</exception>
    /// <exception cref="ArgumentException">Thrown when the specified pin doesn't support the required mode.</exception>
    public GpioLimitSwitch(
        IGpioController gpioController,
        int gpioPinNumber,
        bool isPullup = true,
        bool hasExternalResistor = false,
        TimeSpan debounceTime = default,
        ILogger<GpioLimitSwitch>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gpioController);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gpioPinNumber, 0);
        
        _logger = logger;
        GpioController = gpioController;
        GpioPinNumber = gpioPinNumber;
        IsPullup = isPullup;
        HasExternalResistor = hasExternalResistor;
        DebounceTime = debounceTime;
        
        // Pre-calculate debounce threshold in ticks for high-performance comparisons
        _debounceTicks = (long)(debounceTime.TotalMilliseconds * Stopwatch.Frequency / 1000.0);

        // Determine pin modes based on resistor configuration
        EventPinMode = IsPullup ? PinMode.InputPullUp : PinMode.InputPullDown;
        GpioPinMode = HasExternalResistor ? PinMode.Input : EventPinMode;

        ValidateAndInitialize();
    }

    /// <summary>
    /// Gets a value indicating whether the circuit uses an external pull resistor.
    /// When true, the pin is configured in Input mode; when false, internal pull resistor is used.
    /// </summary>
    public bool HasExternalResistor { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the pin is configured for pull-up (true) or pull-down (false).
    /// Pull-up: pin reads High when switch is open, Low when closed.
    /// Pull-down: pin reads Low when switch is open, High when closed.
    /// </summary>
    public bool IsPullup { get; private init; }

    /// <summary>
    /// Gets the actual GPIO pin mode used for hardware configuration.
    /// This may be Input (with external resistor) or InputPullUp/InputPullDown (with internal resistor).
    /// </summary>
    public PinMode GpioPinMode { get; private init; }

    /// <summary>
    /// Gets the pin mode used for event interpretation logic.
    /// Always InputPullUp or InputPullDown regardless of external resistor configuration.
    /// </summary>
    public PinMode EventPinMode { get; private init; }

    /// <summary>
    /// Gets the GPIO controller instance used for pin operations.
    /// </summary>
    public IGpioController GpioController { get; private init; }

    /// <summary>
    /// Gets the GPIO pin number being monitored.
    /// </summary>
    public int GpioPinNumber { get; private init; }

    /// <summary>
    /// Gets the debounce time used to filter out mechanical switch bounce.
    /// Events occurring within this timeframe are ignored.
    /// </summary>
    public TimeSpan DebounceTime { get; init; }

    /// <summary>
    /// Gets the current cached pin value, avoiding expensive GPIO read operations when possible.
    /// The value is automatically updated when pin state changes occur.
    /// </summary>
    /// <returns>
    /// The cached pin value if available, otherwise reads from GPIO hardware.
    /// Returns PinValue.Low if the instance has been disposed.
    /// </returns>
    public PinValue CurrentPinValue
    {
        get
        {
            if (_disposed) return PinValue.Low;
            
            lock (_objLock)
            {
                // Return cached value if available, otherwise perform GPIO read
                if (!_pinValueInitialized)
                {
                    try
                    {
                        _lastPinValue = GpioController.Read(GpioPinNumber);
                        _pinValueInitialized = true;
                    }
                    catch
                    {
                        // Return safe default value if GPIO read fails
                        return PinValue.Low;
                    }
                }
                return _lastPinValue;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the limit switch is currently triggered.
    /// In normal mode, this is determined by the current pin value and pull-up/pull-down configuration.
    /// In simulation mode, this returns the simulated triggered state.
    /// </summary>
    public bool IsTriggered
    {
        get
        {
            // In simulation mode, return the simulated state
            if (_simulationMode)
            {
                return _simulatedTriggeredState;
            }
            
            // Check if the pin is currently in the triggered state based on pull configuration
            return IsPullup ? CurrentPinValue == PinValue.Low : CurrentPinValue == PinValue.High;
        }
    }

    /// <summary>
    /// Returns a string representation of the limit switch configuration.
    /// </summary>
    /// <returns>A formatted string containing key configuration parameters.</returns>
    public override string ToString() =>
        $"GpioLimitSwitch [Pin={GpioPinNumber}, Mode={GpioPinMode}, Pull={IsPullup}, ExtResistor={HasExternalResistor}]";

    /// <summary>
    /// Occurs when the limit switch state changes after debouncing.
    /// The event provides detailed information about the pin change including timing and pin mode.
    /// </summary>
    public event EventHandler<LimitSwitchTriggeredEventArgs>? LimitSwitchTriggered;

    #region Simulation Support

    /// <summary>
    /// Gets a value indicating whether the limit switch is currently in simulation mode.
    /// When true, the IsTriggered property uses simulated state instead of actual GPIO pin values.
    /// </summary>
    public bool IsSimulationMode => _simulationMode;

    /// <summary>
    /// Enables simulation mode and sets the simulated triggered state.
    /// When in simulation mode, the IsTriggered property returns the simulated state instead of reading the GPIO pin.
    /// </summary>
    /// <param name="simulatedTriggeredState">The simulated triggered state to use.</param>
    public void SetSimulationMode(bool simulatedTriggeredState)
    {
        lock (_objLock)
        {
            _simulationMode = true;
            _simulatedTriggeredState = simulatedTriggeredState;
            _logger?.LogDebug("Simulation mode enabled - Pin {Pin} simulated triggered state: {State}", 
                GpioPinNumber, simulatedTriggeredState);
        }
    }

    /// <summary>
    /// Updates the simulated triggered state when in simulation mode.
    /// This method has no effect if simulation mode is not enabled.
    /// </summary>
    /// <param name="simulatedTriggeredState">The new simulated triggered state.</param>
    public void UpdateSimulatedState(bool simulatedTriggeredState)
    {
        lock (_objLock)
        {
            if (_simulationMode)
            {
                _simulatedTriggeredState = simulatedTriggeredState;
                _logger?.LogDebug("Simulation state updated - Pin {Pin} simulated triggered state: {State}", 
                    GpioPinNumber, simulatedTriggeredState);
            }
            else
            {
                _logger?.LogWarning("Cannot update simulated state - Pin {Pin} is not in simulation mode", GpioPinNumber);
            }
        }
    }

    /// <summary>
    /// Disables simulation mode and returns to normal GPIO pin reading.
    /// </summary>
    public void DisableSimulationMode()
    {
        lock (_objLock)
        {
            _simulationMode = false;
            _simulatedTriggeredState = false;
            _logger?.LogDebug("Simulation mode disabled - Pin {Pin} returning to normal GPIO operation", GpioPinNumber);
        }
    }

    /// <summary>
    /// Simulates a limit switch trigger event by updating the simulated state and firing the appropriate event.
    /// This method enables simulation mode if not already enabled.
    /// </summary>
    /// <param name="isTriggered">Whether the limit switch should be simulated as triggered.</param>
    public void SimulateTrigger(bool isTriggered)
    {
        lock (_objLock)
        {
            // Enable simulation mode if not already enabled
            if (!_simulationMode)
            {
                _simulationMode = true;
                _logger?.LogDebug("Auto-enabled simulation mode for Pin {Pin}", GpioPinNumber);
            }

            var previousState = _simulatedTriggeredState;
            _simulatedTriggeredState = isTriggered;

            // Fire the event if the state changed
            if (previousState != isTriggered)
            {
                var pinEventType = isTriggered 
                    ? (IsPullup ? PinEventTypes.Falling : PinEventTypes.Rising)  // Triggered
                    : (IsPullup ? PinEventTypes.Rising : PinEventTypes.Falling); // Released

                var eventArgs = new LimitSwitchTriggeredEventArgs(
                    pinEventType,
                    GpioPinNumber,
                    EventPinMode,
                    DateTimeOffset.Now);

                _logger?.LogInformation("Simulating limit switch event - Pin {Pin} {EventType} (Triggered: {IsTriggered})", 
                    GpioPinNumber, pinEventType, isTriggered);

                // Fire the event outside the lock to prevent potential deadlocks
                var handler = LimitSwitchTriggered;
                if (handler != null)
                {
                    // Release the lock before calling the event handler
                    Monitor.Exit(_objLock);
                    try
                    {
                        handler(this, eventArgs);
                    }
                    finally
                    {
                        Monitor.Enter(_objLock);
                    }
                }
            }
        }
    }

    #endregion

    /// <summary>
    /// Validates GPIO pin capabilities and initializes the hardware configuration.
    /// Sets up pin modes, registers event callbacks, and caches initial pin state.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the pin doesn't support the required mode.</exception>
    /// <exception cref="InvalidOperationException">Thrown when GPIO initialization fails.</exception>
    private void ValidateAndInitialize()
    {
        // Verify the GPIO controller supports the required pin mode
        if (!GpioController.IsPinModeSupported(GpioPinNumber, GpioPinMode))
        {
            _logger?.LogError("Pin {Pin} does not support mode {Mode}", GpioPinNumber, GpioPinMode);

            var errorMessage = GpioPinMode == PinMode.Input
                ? $"GPIO pin {GpioPinNumber} cannot be configured as Input"
                : $"GPIO pin {GpioPinNumber} cannot be configured as {(IsPullup ? "pull-up" : "pull-down")}. Use an external resistor and set {nameof(HasExternalResistor)}=true";
            
            throw new ArgumentException(errorMessage, nameof(GpioPinNumber));
        }

        // Track initialization state for proper cleanup on failure
        var initState = new InitializationState();

        try
        {
            // Step 1: Handle pre-existing pin state
            // Check if the pin is already open (could be from previous instance or other code)
            if (GpioController.IsPinOpen(GpioPinNumber))
            {
                initState.WasAlreadyOpen = true;
                _logger?.LogWarning("Pin {Pin} is already open. Closing it before re-initialization.", GpioPinNumber);
                
                try
                {
                    GpioController.ClosePin(GpioPinNumber);
                    _logger?.LogDebug("Successfully closed pre-existing pin {Pin}", GpioPinNumber);
                }
                catch (Exception closeEx)
                {
                    _logger?.LogError(closeEx, "Failed to close pre-existing pin {Pin}", GpioPinNumber);
                    throw new InvalidOperationException(
                        $"Cannot initialize GPIO pin {GpioPinNumber}: pin is already open and cannot be closed.", 
                        closeEx);
                }
            }

            // Step 2: Open the pin in the determined mode
            // This configures the hardware with the appropriate pin mode
            try
            {
                GpioController.OpenPin(GpioPinNumber, GpioPinMode);
                initState.PinOpened = true;
                _logger?.LogDebug("Successfully opened GPIO pin {Pin} in mode {Mode}", GpioPinNumber, GpioPinMode);
            }
            catch (Exception openEx)
            {
                _logger?.LogError(openEx, "Failed to open GPIO pin {Pin} in mode {Mode}", GpioPinNumber, GpioPinMode);
                throw new InvalidOperationException(
                    $"Cannot open GPIO pin {GpioPinNumber} in mode {GpioPinMode}. " +
                    $"Pin may be in use by another process or hardware issue.", 
                    openEx);
            }
            
            // Step 3: Cache initial pin state to avoid unnecessary GPIO reads later
            // This provides a baseline value and tests that the pin is readable
            try
            {
                lock (_objLock)
                {
                    _lastPinValue = GpioController.Read(GpioPinNumber);
                    _pinValueInitialized = true;
                    initState.InitialValueCached = true;
                    _logger?.LogDebug("Cached initial pin {Pin} value: {Value}", GpioPinNumber, _lastPinValue);
                }
            }
            catch (Exception readEx)
            {
                _logger?.LogError(readEx, "Failed to read initial value from GPIO pin {Pin}", GpioPinNumber);
                throw new InvalidOperationException(
                    $"Cannot read initial value from GPIO pin {GpioPinNumber}. " +
                    $"Pin may not be properly configured or hardware issue.", 
                    readEx);
            }
            
            // Step 4: Register for pin state change events
            // This enables the limit switch functionality by monitoring pin transitions
            try
            {
                GpioController.RegisterCallbackForPinValueChangedEvent(
                    GpioPinNumber, 
                    PinEventTypes.Falling | PinEventTypes.Rising, 
                    PinStateChanged);
                initState.CallbackRegistered = true;
                _logger?.LogDebug("Successfully registered event callback for pin {Pin}", GpioPinNumber);
            }
            catch (Exception callbackEx)
            {
                _logger?.LogError(callbackEx, "Failed to register event callback for GPIO pin {Pin}", GpioPinNumber);
                throw new InvalidOperationException(
                    $"Cannot register event callback for GPIO pin {GpioPinNumber}. " +
                    $"Pin may not support interrupts or driver issue.", 
                    callbackEx);
            }

            // Success: Log completion of initialization
            _logger?.LogInformation("Successfully initialized GPIO limit switch on pin {Pin} in mode {Mode}", 
                GpioPinNumber, GpioPinMode);
        }
        catch (Exception ex)
        {
            // Comprehensive cleanup on any failure to prevent resource leaks
            _logger?.LogCritical(ex, "Failed to initialize GPIO pin {Pin}. Performing cleanup.", GpioPinNumber);
            
            // Use synchronous cleanup during initialization (constructor context)
            CleanupPartialInitialization(initState, useAsync: false);
            
            // Re-throw the original exception with additional context
            if (ex is InvalidOperationException || ex is ArgumentException)
            {
                throw; // Already properly wrapped
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected error during GPIO limit switch initialization on pin {GpioPinNumber}. " +
                    $"Mode: {GpioPinMode}, Pull-up: {IsPullup}, External resistor: {HasExternalResistor}",
                    ex);
            }
        }
    }

    /// <summary>
    /// Unified cleanup method that handles both synchronous and asynchronous cleanup.
    /// Eliminates code duplication and provides a single maintenance point.
    /// </summary>
    /// <param name="initState">The initialization state tracking what was completed.</param>
    /// <param name="useAsync">Whether to use async delays for hardware settling.</param>
    private void CleanupPartialInitialization(InitializationState initState, bool useAsync)
    {
        var cleanupErrors = new List<Exception>();

        // Step 1: Unregister callback if it was registered
        if (initState.CallbackRegistered)
        {
            try
            {
                GpioController.UnregisterCallbackForPinValueChangedEvent(GpioPinNumber, PinStateChanged);
                _logger?.LogDebug("Unregistered event callback during cleanup for pin {Pin}", GpioPinNumber);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to unregister event callback during cleanup for pin {Pin}", GpioPinNumber);
                cleanupErrors.Add(ex);
            }
        }

        // Step 2: Close pin if we opened it
        if (initState.PinOpened)
        {
            try
            {
                GpioController.ClosePin(GpioPinNumber);
                _logger?.LogDebug("Closed pin during cleanup for pin {Pin}", GpioPinNumber);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to close pin during cleanup for pin {Pin}", GpioPinNumber);
                cleanupErrors.Add(ex);
            }
        }

        // Step 3: Hardware settling delay if there were issues
        if (cleanupErrors.Count > 0)
        {
            try
            {
                if (useAsync)
                {
                    // For async contexts, synchronously wait for Task.Delay
                    Task.Delay(50).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                else
                {
                    // For sync contexts, use Thread.Sleep
                    Thread.Sleep(50);
                }
                _logger?.LogDebug("Applied settling delay after cleanup errors for pin {Pin}", GpioPinNumber);
            }
            catch
            {
                // Ignore delay errors during cleanup
            }
        }

        // Log cleanup summary
        if (cleanupErrors.Count > 0)
        {
            _logger?.LogWarning("Cleanup completed with {ErrorCount} errors for pin {Pin}", 
                cleanupErrors.Count, GpioPinNumber);
        }
        else
        {
            _logger?.LogDebug("Cleanup completed successfully for pin {Pin}", GpioPinNumber);
        }
    }

    /// <summary>
    /// Core disposal logic with unified error handling and resource cleanup.
    /// Handles GPIO event unregistration, pin closure, and resource cleanup.
    /// </summary>
    /// <param name="useAsync">Whether to use async delays for hardware settling.</param>
    private void DisposeCore(bool useAsync = false)
    {
        if (_disposed) return;

        var errors = new List<Exception>();

        // Step 1: Unregister event callbacks first to prevent further notifications
        try
        {
            GpioController.UnregisterCallbackForPinValueChangedEvent(GpioPinNumber, PinStateChanged);
            _logger?.LogDebug("Unregistered event callback for pin {Pin}", GpioPinNumber);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to unregister event callback for pin {Pin}", GpioPinNumber);
            errors.Add(ex);
        }

        // Step 2: Close the GPIO pin if it's currently open
        try
        {
            if (GpioController.IsPinOpen(GpioPinNumber))
            {
                _logger?.LogDebug("Closing GPIO pin {Pin} in mode {Mode}", GpioPinNumber, GpioPinMode);
                GpioController.ClosePin(GpioPinNumber);
                _logger?.LogDebug("Successfully closed GPIO pin {Pin}", GpioPinNumber);
            }
            else
            {
                _logger?.LogDebug("Pin {Pin} is not open. No action taken.", GpioPinNumber);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to close GPIO pin {Pin}", GpioPinNumber);
            errors.Add(ex);
        }

        // Step 3: Stop the timing stopwatch to free resources
        try
        {
            _stopwatch?.Stop();
            _logger?.LogDebug("Stopped timing stopwatch for pin {Pin}", GpioPinNumber);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to stop stopwatch for pin {Pin}", GpioPinNumber);
            errors.Add(ex);
        }

        // Step 4: Hardware settling delay if there were errors
        if (errors.Count > 0)
        {
            try
            {
                if (useAsync)
                {
                    // For async contexts, synchronously wait for Task.Delay
                    Task.Delay(50).ConfigureAwait(false).GetAwaiter().GetResult();
                }
                else
                {
                    // For sync contexts, use Thread.Sleep
                    Thread.Sleep(50);
                }
                _logger?.LogDebug("Applied settling delay after disposal errors for pin {Pin}", GpioPinNumber);
            }
            catch
            {
                // Ignore delay errors during disposal
            }
        }

        // Report disposal completion
        if (errors.Count > 0)
        {
            _logger?.LogWarning("Disposal completed with {ErrorCount} errors for pin {Pin}", 
                errors.Count, GpioPinNumber);
        }
        else
        {
            _logger?.LogInformation("Successfully disposed GpioLimitSwitch for pin {Pin}", GpioPinNumber);
        }
    }

    /// <summary>
    /// Finalizer that ensures proper cleanup of GPIO resources if Dispose is not called.
    /// This is a safety net - proper disposal through IDisposable or IAsyncDisposable is preferred.
    /// </summary>
    ~GpioLimitSwitch()
    {
        Dispose(false);
    }

    /// <summary>
    /// Implements the dispose pattern for proper resource cleanup.
    /// Handles both managed and unmanaged resource disposal based on the disposing parameter.
    /// </summary>
    /// <param name="disposing">
    /// True when called from IDisposable.Dispose() (managed disposal).
    /// False when called from finalizer (unmanaged disposal only).
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            try
            {
                _logger?.LogInformation("Disposing GpioLimitSwitch for pin {Pin}", GpioPinNumber);
                DisposeCore(useAsync: false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during synchronous disposal of GpioLimitSwitch pin {Pin}", GpioPinNumber);
                throw;
            }
        }
        else
        {
            // Finalizer path - suppress all exceptions to prevent application crash
            try
            {
                // Only attempt basic cleanup during finalization
                GpioController?.UnregisterCallbackForPinValueChangedEvent(GpioPinNumber, PinStateChanged);
                if (GpioController?.IsPinOpen(GpioPinNumber) == true)
                {
                    GpioController.ClosePin(GpioPinNumber);
                }
                _stopwatch?.Stop();
            }
            catch
            {
                // Suppress all exceptions during finalization
            }
        }

        _disposed = true;
    }

    /// <summary>
    /// Asynchronously releases all resources used by the GpioLimitSwitch instance.
    /// Provides a small settling delay for debounced operations before cleanup.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            _logger?.LogInformation("Async disposing GpioLimitSwitch for pin {Pin}", GpioPinNumber);
            
            // Allow brief settling time for any pending debounced operations
            if (DebounceTime > TimeSpan.Zero)
            {
                var settlingDelay = Math.Min(50, (int)DebounceTime.TotalMilliseconds);
                _logger?.LogDebug("Waiting {Delay}ms for debounce settling before disposal", settlingDelay);
                await Task.Delay(settlingDelay).ConfigureAwait(false);
            }

            // Perform the actual cleanup with async support
            DisposeCore(useAsync: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during asynchronous disposal of GpioLimitSwitch pin {Pin}", GpioPinNumber);
            throw;
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Synchronously releases all resources used by the GpioLimitSwitch instance.
    /// This is the standard IDisposable implementation.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Handles GPIO pin state change events with high-performance debouncing logic.
    /// Implements a fast-path check followed by thread-safe state updates.
    /// </summary>
    /// <param name="sender">The source of the event (GPIO controller).</param>
    /// <param name="e">Event arguments containing pin change details.</param>
    private void PinStateChanged(object sender, PinValueChangedEventArgs e)
    {
        // Early exit if disposed to prevent processing during cleanup
        if (_disposed) return;

        try
        {
            var currentTicks = _stopwatch.ElapsedTicks;
            var lastTicks = Interlocked.Read(ref _lastEventTicks);

            // Fast-path debounce check without locking for better performance
            // Skip debounce check for the first event (when lastTicks is 0)
            if (lastTicks > 0 && currentTicks - lastTicks < _debounceTicks || 
                (_lastEventType != PinEventTypes.None && _lastEventType == e.ChangeType))
            {
                _logger?.LogDebug("Debounced event on pin {Pin}: {Type} (filtered - too soon after last event)", 
                    e.PinNumber, e.ChangeType);
                return;
            }

            // Determine new pin value based on event type
            var newPinValue = e.ChangeType == PinEventTypes.Rising ? PinValue.High : PinValue.Low;

            // Thread-safe update of both cached pin value and event record
            lock (_objLock)
            {
                // Re-check conditions after acquiring lock to handle race conditions
                // Skip debounce check for the first event (when _lastEventTicks is 0)
                if (_disposed || (_lastEventTicks > 0 && currentTicks - _lastEventTicks < _debounceTicks) || 
                    (_lastEventType != PinEventTypes.None && _lastEventType == e.ChangeType))
                {
                    _logger?.LogDebug("Debounced event on pin {Pin}: {Type} (filtered - race condition detected)", 
                        e.PinNumber, e.ChangeType);
                    return;
                }

                // Update cached pin value and event record atomically
                _lastPinValue = newPinValue;
                _pinValueInitialized = true;
                _lastEventTicks = currentTicks;
                _lastEventType = e.ChangeType;
            }

            // Log the valid limit switch trigger event
            _logger?.LogInformation("Limit switch triggered on pin {Pin}: {Type} -> {Value}", 
                e.PinNumber, e.ChangeType, newPinValue);

            // Create event arguments with current timestamp
            var eventArgs = new LimitSwitchTriggeredEventArgs(
                e.ChangeType,
                e.PinNumber,
                EventPinMode,
                DateTimeOffset.Now);

            // Invoke event handlers outside of lock to prevent potential deadlocks
            try
            {
                LimitSwitchTriggered?.Invoke(this, eventArgs);
            }
            catch (Exception eventEx)
            {
                _logger?.LogError(eventEx, "Error in event handler for limit switch on pin {Pin}", e.PinNumber);
                // Don't re-throw - event handler errors shouldn't crash the GPIO monitoring
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error in PinStateChanged for pin {Pin}", e.PinNumber);
            // Don't re-throw - GPIO event handler errors shouldn't crash the application
        }
    }
}
