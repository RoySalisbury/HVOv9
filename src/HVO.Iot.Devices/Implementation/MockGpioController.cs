using System;
using System.Collections.Concurrent;
using System.Device.Gpio;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

/// <summary>
/// Mock implementation of IGpioController that simulates Raspberry Pi 5 GPIO behavior.
/// This implementation maintains accurate pin states, supports pull-up/pull-down resistors,
/// and provides event handling for testing and development scenarios.
/// </summary>
public class MockGpioController : IGpioController
{
    private readonly ConcurrentDictionary<int, PinState> _pinStates = new();
    private readonly ConcurrentDictionary<int, List<CallbackRegistration>> _eventCallbacks = new();
    private bool _disposed;

    /// <summary>
    /// Represents the state of a GPIO pin including its mode, value, and configuration.
    /// </summary>
    private class PinState
    {
        public PinMode Mode { get; set; }
        public PinValue Value { get; set; }
        public bool IsOpen { get; set; }
        public bool HasExternalPullUp { get; set; }
        public bool HasExternalPullDown { get; set; }

        public PinState()
        {
            Mode = PinMode.Input;
            Value = PinValue.Low;
            IsOpen = false;
            HasExternalPullUp = false;
            HasExternalPullDown = false;
        }
    }

    /// <summary>
    /// Represents a registered callback for pin value change events.
    /// </summary>
    private class CallbackRegistration
    {
        public PinEventTypes EventTypes { get; set; }
        public PinChangeEventHandler Callback { get; set; }

        public CallbackRegistration(PinEventTypes eventTypes, PinChangeEventHandler callback)
        {
            EventTypes = eventTypes;
            Callback = callback;
        }
    }

    /// <summary>
    /// Raspberry Pi 5 GPIO pins that support different pin modes.
    /// Based on the BCM2712 SoC GPIO capabilities.
    /// </summary>
    private static readonly HashSet<int> ValidGpioPins = new()
    {
        // Standard GPIO pins (0-27 are typically available)
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27,
        // Additional pins available on Pi 5 (up to GPIO 47 on BCM2712)
        28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47
    };

    /// <summary>
    /// GPIO pins that support pull-up/pull-down resistors on Raspberry Pi 5.
    /// All GPIO pins on Pi 5 support programmable pull resistors.
    /// </summary>
    private static readonly HashSet<int> PullResistorCapablePins = new(ValidGpioPins);

    /// <summary>
    /// Initializes a new instance of the MockGpioController class.
    /// </summary>
    public MockGpioController()
    {
        // Initialize all valid GPIO pins in their default state (high impedance input)
        foreach (var pin in ValidGpioPins)
        {
            _pinStates[pin] = new PinState();
            _eventCallbacks[pin] = new List<CallbackRegistration>();
        }
    }

    /// <inheritdoc />
    public bool IsPinModeSupported(int pinNumber, PinMode mode)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            return false;

        return mode switch
        {
            PinMode.Input => true,
            PinMode.InputPullDown => PullResistorCapablePins.Contains(pinNumber),
            PinMode.InputPullUp => PullResistorCapablePins.Contains(pinNumber),
            PinMode.Output => true,
            _ => false
        };
    }

    /// <inheritdoc />
    public bool IsPinOpen(int pinNumber)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            return false;

        return _pinStates[pinNumber].IsOpen;
    }

    /// <inheritdoc />
    public void OpenPin(int pinNumber, PinMode mode, PinValue initialValue)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin on Raspberry Pi 5", nameof(pinNumber));

        if (!IsPinModeSupported(pinNumber, mode))
            throw new ArgumentException($"Pin mode {mode} is not supported on pin {pinNumber}", nameof(mode));

        var pinState = _pinStates[pinNumber];
        
        if (pinState.IsOpen)
            throw new InvalidOperationException($"Pin {pinNumber} is already open");

        pinState.IsOpen = true;
        pinState.Mode = mode;
        pinState.Value = initialValue;
        
        // Set initial pin value based on mode and pull resistor configuration
        //SetInitialPinValue(pinNumber, mode);
    }

    /// <inheritdoc />
    public void ClosePin(int pinNumber)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            return;

        var pinState = _pinStates[pinNumber];
        pinState.IsOpen = false;
        pinState.Mode = PinMode.Input;
        pinState.Value = PinValue.Low;
        
        // Clear all event callbacks for this pin
        _eventCallbacks[pinNumber].Clear();
    }

    /// <inheritdoc />
    public PinValue Read(int pinNumber)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));

        var pinState = _pinStates[pinNumber];
        
        if (!pinState.IsOpen)
            throw new InvalidOperationException($"Pin {pinNumber} is not open");

        return pinState.Value;
    }

    /// <inheritdoc />
    public void Write(int pinNumber, PinValue value)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));

        var pinState = _pinStates[pinNumber];
        
        if (!pinState.IsOpen)
            throw new InvalidOperationException($"Pin {pinNumber} is not open");

        if (pinState.Mode != PinMode.Output)
            throw new InvalidOperationException($"Pin {pinNumber} is not configured as output");

        var previousValue = pinState.Value;
        pinState.Value = value;
        
        // Trigger events if value changed
        if (previousValue != value)
        {
            TriggerPinValueChangedEvent(pinNumber, previousValue, value);
        }
    }

    /// <inheritdoc />
    public void RegisterCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));

        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        var pinState = _pinStates[pinNumber];
        if (!pinState.IsOpen)
            throw new InvalidOperationException($"Pin {pinNumber} is not open");

        var registration = new CallbackRegistration(eventTypes, callback);
        _eventCallbacks[pinNumber].Add(registration);
    }

    /// <inheritdoc />
    public void UnregisterCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            return;

        if (callback == null)
            return;

        var callbacks = _eventCallbacks[pinNumber];
        callbacks.RemoveAll(reg => ReferenceEquals(reg.Callback, callback));
    }

    /// <summary>
    /// Simulates an external signal change on the specified pin.
    /// This method is useful for testing scenarios where you need to simulate
    /// hardware events like button presses or sensor triggers.
    /// </summary>
    /// <param name="pinNumber">The pin number to change.</param>
    /// <param name="newValue">The new pin value.</param>
    /// <exception cref="ArgumentException">Thrown when the pin number is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the pin is not open or not configured as input.</exception>
    public void SimulatePinValueChange(int pinNumber, PinValue newValue)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));

        var pinState = _pinStates[pinNumber];
        
        if (!pinState.IsOpen)
            throw new InvalidOperationException($"Pin {pinNumber} is not open");

        if (pinState.Mode == PinMode.Output)
            throw new InvalidOperationException($"Cannot simulate external signal on output pin {pinNumber}");

        var previousValue = pinState.Value;
        pinState.Value = newValue;
        
        // Trigger events if value changed
        if (previousValue != newValue)
        {
            TriggerPinValueChangedEvent(pinNumber, previousValue, newValue);
        }
    }

    /// <summary>
    /// Gets the current pin mode for the specified pin.
    /// This is useful for testing to verify pin configuration.
    /// </summary>
    /// <param name="pinNumber">The pin number to check.</param>
    /// <returns>The current pin mode.</returns>
    /// <exception cref="ArgumentException">Thrown when the pin number is invalid.</exception>
    public PinMode GetPinMode(int pinNumber)
    {
        ThrowIfDisposed();
        
        if (!ValidGpioPins.Contains(pinNumber))
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));

        return _pinStates[pinNumber].Mode;
    }

    /// <summary>
    /// Sets the initial pin value based on the pin mode and Raspberry Pi 5 hardware behavior.
    /// </summary>
    /// <param name="pinNumber">The pin number.</param>
    /// <param name="mode">The pin mode.</param>
    /// <param name="initialValue">The initial value to set for output pins.</param>
    private void SetInitialPinValue(int pinNumber, PinMode mode, PinValue initialValue)
    {
        var pinState = _pinStates[pinNumber];
        
        pinState.Value = mode switch
        {
            PinMode.InputPullUp => PinValue.High,      // Pull-up resistor pulls to VCC (3.3V)
            PinMode.InputPullDown => PinValue.Low,     // Pull-down resistor pulls to GND
            PinMode.Input => PinValue.Low,             // High impedance, typically reads low without external pull
            PinMode.Output => PinValue.Low,            // Output pins default to low
            _ => PinValue.Low
        };
    }

    /// <summary>
    /// Triggers pin value changed events for registered callbacks.
    /// </summary>
    /// <param name="pinNumber">The pin number that changed.</param>
    /// <param name="previousValue">The previous pin value.</param>
    /// <param name="newValue">The new pin value.</param>
    private void TriggerPinValueChangedEvent(int pinNumber, PinValue previousValue, PinValue newValue)
    {
        var callbacks = _eventCallbacks[pinNumber];
        var eventArgs = new PinValueChangedEventArgs(
            GetEventType(previousValue, newValue),
            pinNumber);

        foreach (var registration in callbacks.ToList()) // ToList to avoid modification during iteration
        {
            var eventType = GetEventType(previousValue, newValue);
            if ((registration.EventTypes & eventType) != 0)
            {
                try
                {
                    registration.Callback(this, eventArgs);
                }
                catch
                {
                    // Ignore callback exceptions to prevent one bad callback from affecting others
                }
            }
        }
    }

    /// <summary>
    /// Determines the event type based on the pin value change.
    /// </summary>
    /// <param name="previousValue">The previous pin value.</param>
    /// <param name="newValue">The new pin value.</param>
    /// <returns>The corresponding pin event type.</returns>
    private static PinEventTypes GetEventType(PinValue previousValue, PinValue newValue)
    {
        if (previousValue == PinValue.Low && newValue == PinValue.High)
            return PinEventTypes.Rising;
        
        if (previousValue == PinValue.High && newValue == PinValue.Low)
            return PinEventTypes.Falling;
        
        return PinEventTypes.None;
    }

    /// <summary>
    /// Throws an exception if the instance has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the instance has been disposed.</exception>
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MockGpioController));
    }

    /// <summary>
    /// Disposes the mock GPIO controller and clears all pin states and callbacks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        // Close all open pins
        foreach (var kvp in _pinStates)
        {
            if (kvp.Value.IsOpen)
            {
                ClosePin(kvp.Key);
            }
        }
        
        _pinStates.Clear();
        _eventCallbacks.Clear();
        _disposed = true;
    }
}
