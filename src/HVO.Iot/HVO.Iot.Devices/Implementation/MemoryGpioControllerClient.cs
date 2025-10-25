using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Linq;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

#pragma warning disable CS1591
/// <summary>
/// In-memory implementation of <see cref="IGpioControllerClient"/> that emulates Raspberry Pi 5 GPIO behaviour.
/// Provides deterministic pin state management and event dispatching for automated tests and simulations.
/// </summary>
public class MemoryGpioControllerClient : IGpioControllerClient
{
    private readonly ConcurrentDictionary<int, PinState> _pinStates = new();
    private readonly ConcurrentDictionary<int, List<CallbackRegistration>> _eventCallbacks = new();
    private bool _disposed;

    private class PinState
    {
        public PinMode Mode { get; set; } = PinMode.Input;
        public PinValue Value { get; set; } = PinValue.Low;
        public bool IsOpen { get; set; }
    }

    private class CallbackRegistration
    {
        public PinEventTypes EventTypes { get; }
        public PinChangeEventHandler Callback { get; }

        public CallbackRegistration(PinEventTypes eventTypes, PinChangeEventHandler callback)
        {
            EventTypes = eventTypes;
            Callback = callback;
        }
    }

    private static readonly HashSet<int> ValidGpioPins = new()
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27,
        28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47
    };

    private static readonly HashSet<int> PullResistorCapablePins = new(ValidGpioPins);

    public MemoryGpioControllerClient()
    {
        foreach (var pin in ValidGpioPins)
        {
            _pinStates[pin] = new PinState();
            _eventCallbacks[pin] = new List<CallbackRegistration>();
        }
    }

    public bool IsPinModeSupported(int pinNumber, PinMode mode)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            return false;
        }

        return mode switch
        {
            PinMode.Input => true,
            PinMode.InputPullDown => PullResistorCapablePins.Contains(pinNumber),
            PinMode.InputPullUp => PullResistorCapablePins.Contains(pinNumber),
            PinMode.Output => true,
            _ => false
        };
    }

    public bool IsPinOpen(int pinNumber)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            return false;
        }

        return _pinStates[pinNumber].IsOpen;
    }

    public void OpenPin(int pinNumber, PinMode mode)
    {
        var initial = mode switch
        {
            PinMode.InputPullUp => PinValue.High,
            PinMode.InputPullDown => PinValue.Low,
            PinMode.Input => PinValue.Low,
            PinMode.Output => PinValue.Low,
            _ => PinValue.Low
        };
        OpenPin(pinNumber, mode, initial);
    }

    public void OpenPin(int pinNumber, PinMode mode, PinValue initialValue)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin on Raspberry Pi 5", nameof(pinNumber));
        }

        if (!IsPinModeSupported(pinNumber, mode))
        {
            throw new ArgumentException($"Pin mode {mode} is not supported on pin {pinNumber}", nameof(mode));
        }

        var pinState = _pinStates[pinNumber];

        if (pinState.IsOpen)
        {
            throw new InvalidOperationException($"Pin {pinNumber} is already open");
        }

        pinState.IsOpen = true;
        pinState.Mode = mode;
        pinState.Value = mode switch
        {
            PinMode.InputPullUp => PinValue.High,
            PinMode.InputPullDown => PinValue.Low,
            _ => initialValue
        };
    }

    public void ClosePin(int pinNumber)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            return;
        }

        var pinState = _pinStates[pinNumber];
        pinState.IsOpen = false;
        pinState.Mode = PinMode.Input;
        pinState.Value = PinValue.Low;
        _eventCallbacks[pinNumber].Clear();
    }

    public PinValue Read(int pinNumber)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));
        }

        var pinState = _pinStates[pinNumber];

        if (!pinState.IsOpen)
        {
            throw new InvalidOperationException($"Pin {pinNumber} is not open");
        }

        return pinState.Value;
    }

    public void Write(int pinNumber, PinValue value)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));
        }

        var pinState = _pinStates[pinNumber];

        if (!pinState.IsOpen)
        {
            throw new InvalidOperationException($"Pin {pinNumber} is not open");
        }

        if (pinState.Mode != PinMode.Output)
        {
            throw new InvalidOperationException($"Pin {pinNumber} is not configured as output");
        }

        var previousValue = pinState.Value;
        pinState.Value = value;

        if (previousValue != value)
        {
            TriggerPinValueChangedEvent(pinNumber, previousValue, value);
        }
    }

    public void RegisterCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));
        }

        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        var pinState = _pinStates[pinNumber];
        if (!pinState.IsOpen)
        {
            throw new InvalidOperationException($"Pin {pinNumber} is not open");
        }

        var registration = new CallbackRegistration(eventTypes, callback);
        _eventCallbacks[pinNumber].Add(registration);
    }

    public void UnregisterCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            return;
        }

        if (callback is null)
        {
            return;
        }

        var callbacks = _eventCallbacks[pinNumber];
        callbacks.RemoveAll(reg => ReferenceEquals(reg.Callback, callback));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

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

    public void SimulatePinValueChange(int pinNumber, PinValue newValue)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));
        }

        var pinState = _pinStates[pinNumber];

        if (!pinState.IsOpen)
        {
            throw new InvalidOperationException($"Pin {pinNumber} is not open");
        }

        if (pinState.Mode == PinMode.Output)
        {
            throw new InvalidOperationException($"Cannot simulate external signal on output pin {pinNumber}");
        }

        var previousValue = pinState.Value;
        pinState.Value = newValue;

        if (previousValue != newValue)
        {
            TriggerPinValueChangedEvent(pinNumber, previousValue, newValue);
        }
    }

    public PinMode GetPinMode(int pinNumber)
    {
        ThrowIfDisposed();

        if (!ValidGpioPins.Contains(pinNumber))
        {
            throw new ArgumentException($"Pin {pinNumber} is not a valid GPIO pin", nameof(pinNumber));
        }

        return _pinStates[pinNumber].Mode;
    }

    private void TriggerPinValueChangedEvent(int pinNumber, PinValue previousValue, PinValue newValue)
    {
        var callbacks = _eventCallbacks[pinNumber];
        var eventArgs = new PinValueChangedEventArgs(GetEventType(previousValue, newValue), pinNumber);

        foreach (var registration in callbacks.ToList())
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

    private static PinEventTypes GetEventType(PinValue previousValue, PinValue newValue)
    {
        if (previousValue == PinValue.Low && newValue == PinValue.High)
        {
            return PinEventTypes.Rising;
        }

        if (previousValue == PinValue.High && newValue == PinValue.Low)
        {
            return PinEventTypes.Falling;
        }

        return PinEventTypes.None;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MemoryGpioControllerClient));
        }
    }
}
#pragma warning restore CS1591
