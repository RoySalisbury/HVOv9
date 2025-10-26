using System;
using System.Device.Gpio;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

#pragma warning disable CS1591
/// <summary>
/// Default hardware-backed implementation of <see cref="IGpioControllerClient"/> that wraps
/// <see cref="GpioController"/> from <c>System.Device.Gpio</c>.
/// </summary>
public sealed class GpioControllerClient : IGpioControllerClient
{
    private readonly GpioController _controller;
    private readonly bool _ownsController;
    private bool _disposed;

    /// <summary>
    /// Creates a new client using the default <see cref="GpioController"/> configuration.
    /// </summary>
    public GpioControllerClient()
        : this(new GpioController(), ownsController: true)
    {
    }

    /// <summary>
    /// Creates a new client that wraps an existing <see cref="GpioController"/> instance.
    /// </summary>
    public GpioControllerClient(GpioController controller, bool ownsController = true)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _ownsController = ownsController;
    }

    public bool IsPinModeSupported(int pinNumber, PinMode mode)
    {
        ThrowIfDisposed();
        return _controller.IsPinModeSupported(pinNumber, mode);
    }

    public bool IsPinOpen(int pinNumber)
    {
        ThrowIfDisposed();
        return _controller.IsPinOpen(pinNumber);
    }

    public void OpenPin(int pinNumber, PinMode mode)
    {
        ThrowIfDisposed();
        var initial = mode == PinMode.Output ? PinValue.Low : PinValue.Low;
        _controller.OpenPin(pinNumber, mode, initial);
    }

    public void OpenPin(int pinNumber, PinMode mode, PinValue initialValue)
    {
        ThrowIfDisposed();
        _controller.OpenPin(pinNumber, mode, initialValue);
    }

    public void ClosePin(int pinNumber)
    {
        ThrowIfDisposed();
        _controller.ClosePin(pinNumber);
    }

    public PinValue Read(int pinNumber)
    {
        ThrowIfDisposed();
        return _controller.Read(pinNumber);
    }

    public void Write(int pinNumber, PinValue value)
    {
        ThrowIfDisposed();
        _controller.Write(pinNumber, value);
    }

    public void RegisterCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();
        _controller.RegisterCallbackForPinValueChangedEvent(pinNumber, eventTypes, callback);
    }

    public void UnregisterCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();
        _controller.UnregisterCallbackForPinValueChangedEvent(pinNumber, callback);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsController)
        {
            _controller.Dispose();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GpioControllerClient));
        }
    }
}
#pragma warning restore CS1591
