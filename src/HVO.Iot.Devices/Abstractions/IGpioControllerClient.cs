using System;
using System.Device.Gpio;

namespace HVO.Iot.Devices.Abstractions;

/// <summary>
/// Contract for GPIO controller clients used by HVO device drivers.
/// Mirrors the I²C register client pattern so hardware and simulations can share the same surface area.
/// </summary>
public interface IGpioControllerClient : IDisposable
{
    /// <summary>
    /// Environment variable used to force hardware-backed GPIO implementations.
    /// </summary>
    public const string UseRealHardwareEnvironmentVariable = "USE_REAL_GPIO";

    /// <summary>
    /// Pin value used for boolean <see langword="true"/> conversions.
    /// </summary>
    private static readonly PinValue TruePinValue = PinValue.High;

    /// <summary>
    /// Pin value used for boolean <see langword="false"/> conversions.
    /// </summary>
    private static readonly PinValue FalsePinValue = PinValue.Low;

    /// <summary>
    /// Checks if a pin mode is supported on the specified pin.
    /// </summary>
    bool IsPinModeSupported(int pinNumber, PinMode mode);

    /// <summary>
    /// Checks if a pin is currently open.
    /// </summary>
    bool IsPinOpen(int pinNumber);

    /// <summary>
    /// Opens a pin for GPIO operations with a default initial value (Low for outputs).
    /// </summary>
    void OpenPin(int pinNumber, PinMode mode);

    /// <summary>
    /// Opens a pin for GPIO operations with an explicit initial value for outputs.
    /// </summary>
    void OpenPin(int pinNumber, PinMode mode, PinValue initialValue);

    /// <summary>
    /// Closes a pin and releases its resources.
    /// </summary>
    void ClosePin(int pinNumber);

    /// <summary>
    /// Reads the current value of a pin.
    /// </summary>
    PinValue Read(int pinNumber);

    /// <summary>
    /// Reads the current value of a pin as a boolean where <see langword="true"/> represents <see cref="PinValue.High"/>.
    /// </summary>
    bool ReadAsBoolean(int pinNumber) => ConvertToBoolean(Read(pinNumber));

    /// <summary>
    /// Writes a value to a pin.
    /// </summary>
    void Write(int pinNumber, PinValue value);

    /// <summary>
    /// Writes a boolean value to a pin, mapping <see langword="true"/> to <see cref="PinValue.High"/>.
    /// </summary>
    void Write(int pinNumber, bool value) => Write(pinNumber, ConvertToPinValue(value));

    /// <summary>
    /// Writes a <see cref="PinValue.High"/> value to the specified pin.
    /// </summary>
    void WriteHigh(int pinNumber) => Write(pinNumber, TruePinValue);

    /// <summary>
    /// Writes a <see cref="PinValue.Low"/> value to the specified pin.
    /// </summary>
    void WriteLow(int pinNumber) => Write(pinNumber, FalsePinValue);

    /// <summary>
    /// Registers a callback for pin value change events.
    /// </summary>
    void RegisterCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback);

    /// <summary>
    /// Unregisters a callback for pin value change events.
    /// </summary>
    void UnregisterCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback);

    private static PinValue ConvertToPinValue(bool value) => value ? TruePinValue : FalsePinValue;

    private static bool ConvertToBoolean(PinValue value) => value == TruePinValue;
}
