using System;
using System.Device.Gpio;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Implementation;

/// <summary>
/// Wrapper implementation of IGpioController that adapts the System.Device.Gpio.GpioController.
/// This enables dependency injection and testability while maintaining compatibility with the GPIO hardware.
/// Automatically selects between real GPIO hardware and mock controller based on environment and platform.
/// </summary>
public class GpioControllerWrapper : IGpioController
{
    private readonly IGpioController _gpioController;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the GpioControllerWrapper class.
    /// </summary>
    /// <param name="gpioController">The underlying GPIO controller to wrap. If null, will auto-select based on environment.</param>
    /// <param name="useRealHardware">Optional override to force real hardware usage. If null, uses USE_REAL_GPIO environment variable.</param>
    public GpioControllerWrapper(IGpioController? gpioController = null, bool? useRealHardware = null)
    {
        if (gpioController != null)
        {
            _gpioController = gpioController;
        }
        else
        {
            // Determine whether to use real hardware
            bool shouldUseRealHardware = useRealHardware ?? 
                (System.Environment.GetEnvironmentVariable("USE_REAL_GPIO") == "true");

            if (shouldUseRealHardware)
            {
                // Validate platform when attempting to use real GPIO hardware
                if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                {
                    throw new PlatformNotSupportedException(
                        "Real GPIO hardware (USE_REAL_GPIO=true) is only supported on Linux/Raspberry Pi. " +
                        "Use MockGpioController for development on other platforms by setting USE_REAL_GPIO=false or leaving it unset.");
                }
                
                // Use real GPIO hardware by wrapping System.Device.Gpio.GpioController
                _gpioController = new SystemGpioControllerAdapter(new GpioController());
            }
            else
            {
                // Use mock GPIO controller that emulates hardware behavior
                _gpioController = new MockGpioController();
            }
        }
    }

    /// <summary>
    /// Factory method to create a GpioControllerWrapper with auto-selection logic.
    /// This method helps avoid circular dependency issues in DI containers.
    /// </summary>
    /// <param name="useRealHardware">Optional override to force real hardware usage. If null, uses USE_REAL_GPIO environment variable.</param>
    /// <returns>A new GpioControllerWrapper instance with appropriate underlying controller.</returns>
    public static GpioControllerWrapper CreateAutoSelecting(bool? useRealHardware = null)
    {
        // Determine whether to use real hardware
        bool shouldUseRealHardware = useRealHardware ?? 
            (System.Environment.GetEnvironmentVariable("USE_REAL_GPIO") == "true");

        IGpioController underlyingController;

        if (shouldUseRealHardware)
        {
            // Validate platform when attempting to use real GPIO hardware
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException(
                    "Real GPIO hardware (USE_REAL_GPIO=true) is only supported on Linux/Raspberry Pi. " +
                    "Use MockGpioController for development on other platforms by setting USE_REAL_GPIO=false or leaving it unset.");
            }
            
            // Use real GPIO hardware by wrapping System.Device.Gpio.GpioController
            underlyingController = new SystemGpioControllerAdapter(new GpioController());
        }
        else
        {
            // Use mock GPIO controller that emulates hardware behavior
            underlyingController = new MockGpioController();
        }

        return new GpioControllerWrapper(underlyingController);
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

/// <summary>
/// Internal adapter to wrap System.Device.Gpio.GpioController and implement IGpioController interface.
/// </summary>
internal class SystemGpioControllerAdapter : IGpioController
{
    private readonly GpioController _systemGpioController;
    private bool _disposed;

    public SystemGpioControllerAdapter(GpioController systemGpioController)
    {
        _systemGpioController = systemGpioController ?? throw new ArgumentNullException(nameof(systemGpioController));
    }

    public bool IsPinModeSupported(int pinNumber, PinMode mode)
    {
        ThrowIfDisposed();
        return _systemGpioController.IsPinModeSupported(pinNumber, mode);
    }

    public bool IsPinOpen(int pinNumber)
    {
        ThrowIfDisposed();
        return _systemGpioController.IsPinOpen(pinNumber);
    }

    public void OpenPin(int pinNumber, PinMode mode)
    {
        ThrowIfDisposed();
        _systemGpioController.OpenPin(pinNumber, mode);
    }

    public void ClosePin(int pinNumber)
    {
        ThrowIfDisposed();
        _systemGpioController.ClosePin(pinNumber);
    }

    public PinValue Read(int pinNumber)
    {
        ThrowIfDisposed();
        return _systemGpioController.Read(pinNumber);
    }

    public void Write(int pinNumber, PinValue value)
    {
        ThrowIfDisposed();
        _systemGpioController.Write(pinNumber, value);
    }

    public void RegisterCallbackForPinValueChangedEvent(int pinNumber, PinEventTypes eventTypes, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();
        _systemGpioController.RegisterCallbackForPinValueChangedEvent(pinNumber, eventTypes, callback);
    }

    public void UnregisterCallbackForPinValueChangedEvent(int pinNumber, PinChangeEventHandler callback)
    {
        ThrowIfDisposed();
        _systemGpioController.UnregisterCallbackForPinValueChangedEvent(pinNumber, callback);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SystemGpioControllerAdapter));
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _systemGpioController?.Dispose();
        _disposed = true;
    }
}
