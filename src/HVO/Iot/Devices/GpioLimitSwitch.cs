using System;
using System.Device.Gpio;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HVO.Iot.Devices;

/// <summary>
/// Represents a GPIO-based limit switch with support for internal/external pull resistors, debounce handling, and logging.
/// </summary>
public class GpioLimitSwitch : IAsyncDisposable, IDisposable
{
    private readonly ILogger<GpioLimitSwitch>? _logger;

    // Records the last event timestamp and type
    protected (DateTimeOffset lastEventTimestamp, PinEventTypes lastEventType) _lastEventRecord = new(DateTimeOffset.MinValue, PinEventTypes.None);
    private readonly object _objLock = new();
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpioLimitSwitch"/> class.
    /// </summary>
    /// <param name="gpioController">The GPIO controller to use (optional).</param>
    /// <param name="gpioPinNumber">The GPIO pin number to monitor.</param>
    /// <param name="isPullup">Use pull-up (true) or pull-down (false) resistor.</param>
    /// <param name="hasExternalResistor">Whether the circuit uses an external resistor.</param>
    /// <param name="debounceTime">Optional debounce time.</param>
    /// <param name="logger">Optional logger instance.</param>
    public GpioLimitSwitch(
        GpioController gpioController,
        int gpioPinNumber,
        bool isPullup = true,
        bool hasExternalResistor = false,
        TimeSpan debounceTime = default,
        ILogger<GpioLimitSwitch>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gpioController);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gpioPinNumber, 0);
        _logger = logger;

        // Initialize the GPIO controller and pin number
        GpioController = gpioController;
        GpioPinNumber = gpioPinNumber;
        IsPullup = isPullup;
        HasExternalResistor = hasExternalResistor;
        DebounceTime = debounceTime;

        // Determine the event and pin modes based on pull-up/pull-down configuration
        EventPinMode = IsPullup ? PinMode.InputPullUp : PinMode.InputPullDown;
        GpioPinMode = HasExternalResistor ? PinMode.Input : EventPinMode;

        // Check if the GPIO controller supports the required pin mode
        if (!GpioController.IsPinModeSupported(GpioPinNumber, GpioPinMode))
        {
            _logger?.LogError("Pin {Pin} does not support mode {Mode}", GpioPinNumber, GpioPinMode);

            if (GpioPinMode == PinMode.Input)
            {
                throw new ArgumentException($"Gpio pin '{GpioPinNumber}' cannot be configured as Input");
            }

            throw new ArgumentException($"Pin {GpioPinNumber} cannot be configured as {(IsPullup ? "pull-up" : "pull-down")}. Use an external resistor and set {nameof(HasExternalResistor)}=true");
        }

        try
        {
            // Open the pin in the specified mode and register for pin value change events
            GpioController.OpenPin(GpioPinNumber, GpioPinMode);
            GpioController.RegisterCallbackForPinValueChangedEvent(GpioPinNumber, PinEventTypes.Falling | PinEventTypes.Rising, PinStateChanged);

            _logger?.LogInformation("Initialized GPIO pin {Pin} in mode {Mode}", GpioPinNumber, GpioPinMode);
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Failed to initialize GPIO pin {Pin}", GpioPinNumber);
            throw;
        }
    }

    // Properties to configure the limit switch
    public bool HasExternalResistor { get; private init; }
    public bool IsPullup { get; private init; }
    public PinMode GpioPinMode { get; private init; }
    public PinMode EventPinMode { get; private init; }
    public GpioController GpioController { get; private init; }
    public int GpioPinNumber { get; private init; }
    public TimeSpan DebounceTime { get; init; }

    // Current pin value read from the GPIO controller
    public PinValue CurrentPinValue => GpioController.Read(GpioPinNumber);

    // Override ToString method to provide a string representation of the limit switch
    public override string ToString() =>
        $"GpioLimitSwitch [Pin={GpioPinNumber}, Mode={GpioPinMode}, Pull={IsPullup}, ExtResistor={HasExternalResistor}]";

    // Event triggered when the limit switch state changes
    public event EventHandler<LimitSwitchTriggeredEventArgs>? LimitSwitchTriggered;

    private void PinStateChanged(object sender, PinValueChangedEventArgs e)
    {
        var currentDateTime = DateTimeOffset.Now;
        var (lastEventTimestamp, lastEventType) = _lastEventRecord;

        lock (_objLock)
        {
            // Check for debounce conditions
            if (currentDateTime - lastEventTimestamp < DebounceTime || (lastEventType != PinEventTypes.None && lastEventType == e.ChangeType))
            {
                _logger?.LogDebug("Debounced event on pin {Pin}: {Type}", e.PinNumber, e.ChangeType);
                return;
            }

            // Update the last event record
            _lastEventRecord = (currentDateTime, e.ChangeType);
        }

        // Log the limit switch trigger and raise the event
        _logger?.LogInformation("Limit switch triggered on pin {Pin}: {Type}", e.PinNumber, e.ChangeType);

        LimitSwitchTriggered?.Invoke(this, new(
            e.ChangeType,
            e.PinNumber,
            this.EventPinMode,
            currentDateTime));
    }

    /// <summary>
    /// Finalizer (destructor) to ensure proper cleanup of unmanaged resources when object is garbage collected.
    /// This is a safety net and should rarely be called as proper disposal should be handled through IDisposable or IAsyncDisposable.
    /// </summary>
    ~GpioLimitSwitch()
    {
        // Pass false because we're in the finalizer and cannot access managed objects safely
        Dispose(false);
    }

    /// <summary>
    /// Protected virtual disposal method that implements the dispose pattern.
    /// This can be overridden in derived classes to add additional cleanup logic.
    /// </summary>
    /// <param name="disposing">
    /// True when called from IDisposable.Dispose(), false when called from finalizer.
    /// When false, only cleanup unmanaged resources as managed objects may have been finalized.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return; // Prevent multiple disposal

        if (disposing)  
        {
            try
            {
                // In the synchronous disposal path, we block on the async disposal
                // This is acceptable here because:
                // 1. We're already in a blocking disposal path
                // 2. We need to ensure proper cleanup of GPIO resources
                // 3. The async disposal handles proper signal settling
                DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during disposal of GpioLimitSwitch pin {Pin}", GpioPinNumber);
                throw;
            }
        }

        _disposed = true;
    }

    /// <summary>
    /// Core async disposal logic shared between sync and async disposal paths.
    /// This method handles the actual cleanup of GPIO resources with proper timing.
    /// </summary>
    /// <returns>A ValueTask representing the async disposal operation.</returns>
    private async ValueTask DisposeAsyncCore()
    {
        if (_disposed) return; // Prevent multiple disposal

        try
        {
            _logger?.LogInformation("Disposing GpioLimitSwitch for pin {Pin}", GpioPinNumber);

            // Order of operations is important:
            // 1. Unregister events to prevent callbacks during cleanup
            GpioController.UnregisterCallbackForPinValueChangedEvent(GpioPinNumber, PinStateChanged);

            // 2. Wait for GPIO signals to settle
            await Task.Delay(50).ConfigureAwait(false);

            // 3. Close the pin asynchronously to avoid blocking
            // Use Task.Run because GpioController methods are synchronous
            await Task.Run(() => GpioController.ClosePin(GpioPinNumber)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during async disposal of GpioLimitSwitch pin {Pin}", GpioPinNumber);
            throw;
        }
    }

    /// <summary>
    /// Asynchronously releases all resources used by the GpioLimitSwitch instance.
    /// This is the preferred disposal method as it allows for proper GPIO signal settling.
    /// </summary>
    /// <returns>A ValueTask representing the async disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; // Prevent multiple disposal

        try
        {
            await DisposeAsyncCore().ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this); // Prevent finalizer from running as we've cleaned up
        }
    }

    /// <summary>
    /// Synchronously releases all resources used by the GpioLimitSwitch instance.
    /// This method blocks while waiting for GPIO operations to complete.
    /// Consider using DisposeAsync for better performance.
    /// </summary>
    public void Dispose()
    {
        Dispose(true); // Pass true for disposing from IDisposable.Dispose
        GC.SuppressFinalize(this); // Prevent finalizer from running as we've cleaned up
    }
}
