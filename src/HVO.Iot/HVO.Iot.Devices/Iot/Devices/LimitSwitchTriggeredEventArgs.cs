using System;
using System.Device.Gpio;

namespace HVO.Iot.Devices;

/// <summary>
/// Provides event data for a triggered GPIO limit switch,
/// including the pin number, change type, pin mode, and event timestamp.
/// </summary>
public class LimitSwitchTriggeredEventArgs(
    PinEventTypes changeType,
    int pinNumber,
    PinMode pinMode,
    DateTimeOffset eventDateTime = default)
    : PinValueChangedEventArgs(changeType, pinNumber)
{
    /// <summary>
    /// Gets the pin mode that was active at the time of the event (e.g., InputPullUp, Input).
    /// </summary>
    public PinMode PinMode { get; init; } = pinMode;

    /// <summary>
    /// Gets the timestamp when the pin value change event was registered.
    /// If no timestamp was provided, the current time is used.
    /// </summary>
    public DateTimeOffset EventDateTime { get; init; } = eventDateTime == default ? DateTimeOffset.Now : eventDateTime;
}
