using System;
using System.Device.Gpio;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

/// <summary>
/// Wrapper implementation of IGpioController that adapts the System.Device.Gpio.GpioController.
/// This enables dependency injection and testability while maintaining compatibility with the GPIO hardware.
/// </summary>
public class GpioControllerWrapper : IGpioController
{
    private readonly GpioController _gpioController;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the GpioControllerWrapper class.
    /// </summary>
    /// <param name="gpioController">The underlying GPIO controller to wrap.</param>
    public GpioControllerWrapper(GpioController? gpioController)
    {
        _gpioController = gpioController ?? throw new ArgumentNullException(nameof(gpioController));
    }

    /// <summary>
    /// Initializes a new instance of the GpioControllerWrapper class with default GPIO controller.
    /// </summary>
    public GpioControllerWrapper() : this(new GpioController())
    {
    }

    /// <inheritdoc />
    public bool IsPinModeSupported(int pinNumber, PinMode mode)
    {
        ThrowIfDisposed();
        return _gpioController.IsPinModeSupported(pinNumber, mode);
    }

    /// <inheritdoc />
    public bool IsPinOpen(int pinNumber)
    {
        ThrowIfDisposed();
        return _gpioController.IsPinOpen(pinNumber);
    }

    /// <inheritdoc />
    public void OpenPin(int pinNumber, PinMode mode)
    {
        ThrowIfDisposed();
        _gpioController.OpenPin(pinNumber, mode);
    }

    /// <inheritdoc />
    public void ClosePin(int pinNumber)
    {
        ThrowIfDisposed();
        _gpioController.ClosePin(pinNumber);
    }

    /// <inheritdoc />
    public PinValue Read(int pinNumber)
    {
        ThrowIfDisposed();
        return _gpioController.Read(pinNumber);
    }

    /// <inheritdoc />
    public void Write(int pinNumber, PinValue value)
    {
        ThrowIfDisposed();
        _gpioController.Write(pinNumber, value);
    }

    /// <inheritdoc />
    public void RegisterCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();
        _gpioController.RegisterCallbackForPinValueChangedEvent(pinNumber, eventTypes, callback);
    }

    /// <inheritdoc />
    public void UnregisterCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();
        _gpioController.UnregisterCallbackForPinValueChangedEvent(pinNumber, callback);
    }

    /// <summary>
    /// Throws an exception if the instance has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the instance has been disposed.</exception>
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GpioControllerWrapper));
    }

    /// <summary>
    /// Disposes the GPIO controller wrapper and underlying controller.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _gpioController?.Dispose();
        _disposed = true;
    }
}
