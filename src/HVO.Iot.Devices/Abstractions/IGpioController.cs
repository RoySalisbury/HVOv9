using System;
using System.Device.Gpio;

namespace HVO.Iot.Devices.Abstractions;

/// <summary>
/// Abstraction interface for GPIO controller operations.
/// This interface enables dependency injection and mocking for testing.
/// </summary>
public interface IGpioController : IDisposable
{
    /// <summary>
    /// Checks if a pin mode is supported on the specified pin.
    /// </summary>
    /// <param name="pinNumber">The pin number to check.</param>
    /// <param name="mode">The pin mode to check for support.</param>
    /// <returns>True if the pin mode is supported, false otherwise.</returns>
    bool IsPinModeSupported(int pinNumber, PinMode mode);

    /// <summary>
    /// Checks if a pin is currently open.
    /// </summary>
    /// <param name="pinNumber">The pin number to check.</param>
    /// <returns>True if the pin is open, false otherwise.</returns>
    bool IsPinOpen(int pinNumber);

    /// <summary>
    /// Opens a pin for GPIO operations.
    /// </summary>
    /// <param name="pinNumber">The pin number to open.</param>
    /// <param name="mode">The mode to configure the pin in.</param>
    void OpenPin(int pinNumber, PinMode mode);

    /// <summary>
    /// Closes a pin and releases its resources.
    /// </summary>
    /// <param name="pinNumber">The pin number to close.</param>
    void ClosePin(int pinNumber);

    /// <summary>
    /// Reads the current value of a pin.
    /// </summary>
    /// <param name="pinNumber">The pin number to read.</param>
    /// <returns>The current pin value.</returns>
    PinValue Read(int pinNumber);

    /// <summary>
    /// Writes a value to a pin.
    /// </summary>
    /// <param name="pinNumber">The pin number to write to.</param>
    /// <param name="value">The value to write.</param>
    void Write(int pinNumber, PinValue value);

    /// <summary>
    /// Registers a callback for pin value change events.
    /// </summary>
    /// <param name="pinNumber">The pin number to monitor.</param>
    /// <param name="eventTypes">The types of events to monitor.</param>
    /// <param name="callback">The callback to invoke when events occur.</param>
    void RegisterCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback);

    /// <summary>
    /// Unregisters a callback for pin value change events.
    /// </summary>
    /// <param name="pinNumber">The pin number to stop monitoring.</param>
    /// <param name="callback">The callback to remove.</param>
    void UnregisterCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback);
}
