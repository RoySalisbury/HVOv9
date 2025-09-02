using System;
using System.Device.Gpio;
using System.IO;
using System.Runtime.InteropServices;
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
    /// Gets the underlying GPIO controller instance for testing purposes.
    /// </summary>
    public IGpioController UnderlyingController => _gpioController;

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
            return;
        }

        // When caller explicitly sets useRealHardware:
        if (useRealHardware.HasValue)
        {
            if (useRealHardware.Value)
            {
                if (TryCreateRealController(out var realController))
                {
                    _gpioController = realController;
                }
                else
                {
                    _gpioController = new MockGpioController();
                }
            }
            else
            {
                _gpioController = new MockGpioController();
            }
            return;
        }

        // Auto selection path (same logic as CreateAutoSelecting)
        _gpioController = SelectAutoController();
    }

    /// <summary>
    /// Factory method to create a GpioControllerWrapper with auto-selection logic.
    /// This method helps avoid circular dependency issues in DI containers.
    /// </summary>
    /// <param name="useRealHardware">Optional override to force real hardware usage. If null, uses USE_REAL_GPIO environment variable.</param>
    /// <returns>A new GpioControllerWrapper instance with appropriate underlying controller.</returns>
    public static GpioControllerWrapper CreateAutoSelecting(bool? useRealHardware = null)
    {
        // Explicit override (true = force attempt real, false = force mock)
        if (useRealHardware.HasValue)
        {
            if (useRealHardware.Value)
            {
                if (TryCreateRealController(out var realController))
                {
                    return new GpioControllerWrapper(realController, useRealHardware: true);
                }
                return new GpioControllerWrapper(new MockGpioController(), useRealHardware: false);
            }
            return new GpioControllerWrapper(new MockGpioController(), useRealHardware: false);
        }

        // Auto selection path when parameter is null
        return new GpioControllerWrapper(SelectAutoController(), useRealHardware: null);
    }

    /// <summary>
    /// Implements the auto selection logic described in requirements.
    /// </summary>
    private static IGpioController SelectAutoController()
    {
        // 1. Check environment variable USE_REAL_GPIO (true => attempt real first)
        var envValue = Environment.GetEnvironmentVariable("USE_REAL_GPIO");
        var envRequestsReal = string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase);

        if (envRequestsReal)
        {
            if (TryCreateRealController(out var realFromEnv))
            {
                return realFromEnv;
            }
            // fall through to other heuristics if creation failed
        }

        // 2. If running on Raspberry Pi, attempt real
        if (IsRaspberryPi())
        {
            if (TryCreateRealController(out var realOnPi))
            {
                return realOnPi;
            }
        }

        // 3. If on another platform but real controller is available, use it
        if (TryCreateRealController(out var realOtherPlatform))
        {
            return realOtherPlatform;
        }

        // 4. Fallback: mock
        return new MockGpioController();
    }

    /// <summary>
    /// Attempts to create a real System.Device.Gpio.GpioController wrapped in an adapter.
    /// Returns false if not available / not supported.
    /// </summary>
    private static bool TryCreateRealController(out IGpioController controller)
    {
        try
        {
            controller = new SystemGpioControllerAdapter(new GpioController());
            return true;
        }
        catch
        {
            controller = default!;
            return false;
        }
    }

    /// <summary>
    /// Rough detection for Raspberry Pi based on Linux platform &amp; device tree / cpuinfo contents.
    /// </summary>
    private static bool IsRaspberryPi()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return false;
        try
        {
            if (File.Exists("/proc/device-tree/model"))
            {
                var model = File.ReadAllText("/proc/device-tree/model");
                if (model.Contains("Raspberry", StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (File.Exists("/proc/cpuinfo"))
            {
                var cpuInfo = File.ReadAllText("/proc/cpuinfo");
                if (cpuInfo.Contains("Raspberry Pi", StringComparison.OrdinalIgnoreCase) || cpuInfo.Contains("BCM", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { /* ignore */ }
        return false;
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
