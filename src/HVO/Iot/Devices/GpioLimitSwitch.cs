using System;
using System.Device.Gpio;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HVO.Iot.Devices;

/// <summary>
/// Represents a GPIO-based limit switch with support for internal/external pull resistors, debounce handling, and logging.
/// </summary>
public class GpioLimitSwitch : IDisposable
{
    private readonly bool _shouldDispose;
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
    /// <param name="shouldDispose">Dispose the GPIO controller when done.</param>
    /// <param name="debounceTime">Optional debounce time.</param>
    /// <param name="logger">Optional logger instance.</param>
    public GpioLimitSwitch(
        GpioController gpioController,
        int gpioPinNumber,
        bool isPullup = true,
        bool hasExternalResistor = false,
        bool shouldDispose = true,
        TimeSpan debounceTime = default,
        ILogger<GpioLimitSwitch>? logger = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gpioPinNumber, 0);
        _logger = logger;

        // Initialize the GPIO controller and pin number
        GpioController = gpioController ?? new GpioController();
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

        _shouldDispose = gpioController == null || shouldDispose;
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

            if (_shouldDispose)
            {
                GpioController?.Dispose();
            }

            throw;
        }
    }

    // Properties to configure the limit switch
    public bool HasExternalResistor { get; init; }
    public bool IsPullup { get; init; }
    public PinMode GpioPinMode { get; init; }
    public PinMode EventPinMode { get; init; }
    public GpioController GpioController { get; init; }
    public int GpioPinNumber { get; init; }
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
        var priorEventRecord = _lastEventRecord;

        lock (_objLock)
        {
            // Check for debounce conditions
            if (currentDateTime - priorEventRecord.lastEventTimestamp < DebounceTime ||
                (priorEventRecord.lastEventType != PinEventTypes.None && priorEventRecord.lastEventType == e.ChangeType))
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

    // Finalizer to ensure proper disposal
    ~GpioLimitSwitch()
    {
        Dispose(false);
    }

    // Public Dispose method
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Protected virtual Dispose method for cleanup
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _logger?.LogInformation("Disposing GpioLimitSwitch for pin {Pin}", GpioPinNumber);
            GpioController.UnregisterCallbackForPinValueChangedEvent(GpioPinNumber, PinStateChanged);

            if (_shouldDispose)
            {
                _logger?.LogDebug("Disposing GpioController instance.");
                GpioController?.Dispose();
            }
            else
            {
                GpioController.ClosePin(GpioPinNumber);
            }
        }

        _disposed = true;
    }
}
