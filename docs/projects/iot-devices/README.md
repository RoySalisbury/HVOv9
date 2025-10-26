# IoT Devices Development Guide

This guide covers the development, testing, and deployment of IoT hardware device abstractions in the HVO.Iot domain.

## Overview

The HVO.Iot.Devices library provides platform-agnostic abstractions for physical hardware devices used throughout the HVOv9 observatory. These abstractions enable testing without hardware and ensure consistent behavior across different platforms.

## Architecture

### Device Abstraction Hierarchy

```
IDisposable
    ├── IGpioDevice (base interface)
    │   ├── GpioLimitSwitch
    │   ├── GpioRelay
    │   └── GpioMotorController
    └── ISensorDevice (future)
        ├── TemperatureSensor
        └── HumiditySensor
```

### Core Interfaces

```csharp
public interface IGpioDevice : IDisposable
{
    int PinNumber { get; }
    bool IsInitialized { get; }
    event EventHandler<EventArgs>? StateChanged;
}

public interface IInputDevice : IGpioDevice
{
    bool IsActive { get; }
}

public interface IOutputDevice : IGpioDevice
{
    void SetState(bool active);
    bool GetState();
}
```

## Device Implementation Patterns

### 1. GpioLimitSwitch Pattern

This is the exemplary implementation that all other devices should follow:

```csharp
public class GpioLimitSwitch : IInputDevice
{
    private readonly int _pinNumber;
    private readonly SwitchPolarity _polarity;
    private readonly IGpioController _controller;
    private readonly ILogger<GpioLimitSwitch>? _logger;
    private readonly object _lock = new();
    
    private bool _isClosed;
    private bool _disposed;
    
    public GpioLimitSwitch(
        int pinNumber, 
        SwitchPolarity polarity, 
        IGpioController controller,
        ILogger<GpioLimitSwitch>? logger = null)
    {
        _pinNumber = pinNumber;
        _polarity = polarity;
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger;
        
        Initialize();
    }
    
    private void Initialize()
    {
        try
        {
            _controller.OpenPin(_pinNumber, PinMode.InputPullUp);
            _controller.RegisterCallbackForPinValueChangedEvent(_pinNumber, PinEventTypes.Both, OnPinValueChanged);
            
            // Read initial state
            var currentValue = _controller.Read(_pinNumber);
            _isClosed = DetermineSwitchState(currentValue);
            
            _logger?.LogDebug("Limit switch initialized - Pin: {PinNumber}, Polarity: {Polarity}, Initial State: {State}", 
                _pinNumber, _polarity, _isClosed ? "CLOSED" : "OPEN");
                
            IsInitialized = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize limit switch on pin {PinNumber}", _pinNumber);
            throw;
        }
    }
    
    private void OnPinValueChanged(object sender, PinValueChangedEventArgs e)
    {
        if (_disposed) return;
        
        lock (_lock)
        {
            var newState = DetermineSwitchState(e.PinValue);
            
            _logger?.LogTrace("Pin {PinNumber} changed to {PinValue}, switch now {SwitchState}", 
                _pinNumber, e.PinValue, newState ? "CLOSED" : "OPEN");
            
            if (newState != _isClosed)
            {
                _isClosed = newState;
                _logger?.LogDebug("Limit switch state changed to {State}", newState ? "CLOSED" : "OPEN");
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    
    private bool DetermineSwitchState(PinValue pinValue)
    {
        return _polarity switch
        {
            SwitchPolarity.NormallyOpen => pinValue == PinValue.Low,   // Active low (pulled down when closed)
            SwitchPolarity.NormallyClosed => pinValue == PinValue.High, // Active high (pulled up when open)
            _ => throw new InvalidOperationException($"Unknown polarity: {_polarity}")
        };
    }
    
    public bool IsClosed
    {
        get
        {
            lock (_lock)
            {
                return _isClosed;
            }
        }
    }
    
    public bool IsActive => IsClosed;
    public int PinNumber => _pinNumber;
    public bool IsInitialized { get; private set; }
    
    public event EventHandler<EventArgs>? StateChanged;
    
    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _controller.UnregisterCallbackForPinValueChangedEvent(_pinNumber, OnPinValueChanged);
            _controller.ClosePin(_pinNumber);
            
            _logger?.LogDebug("Limit switch disposed - Pin: {PinNumber}", _pinNumber);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error disposing limit switch on pin {PinNumber}", _pinNumber);
        }
        finally
        {
            _disposed = true;
        }
    }
    
    public enum SwitchPolarity
    {
        NormallyOpen,    // Circuit open when not activated
        NormallyClosed   // Circuit closed when not activated  
    }
}
```

### 2. Logging Standards

All devices must implement consistent logging patterns:

```csharp
public class DeviceLoggingStandards
{
    // Constructor logging
    _logger?.LogDebug("Device initialized - Pin: {PinNumber}, Configuration: {Config}", 
        pinNumber, configurationDetails);
    
    // State change logging
    _logger?.LogTrace("Pin {PinNumber} signal: {Signal} -> Device state: {State}", 
        pinNumber, signalValue, deviceState);
    
    _logger?.LogDebug("Device state changed: {OldState} -> {NewState}", 
        oldState, newState);
    
    // Error logging
    _logger?.LogError(ex, "Device operation failed - Pin: {PinNumber}, Operation: {Operation}", 
        pinNumber, operationName);
    
    // Performance logging
    _logger?.LogTrace("Operation completed - Duration: {Duration}ms", 
        stopwatch.ElapsedMilliseconds);
    
    // Disposal logging
    _logger?.LogDebug("Device disposed - Pin: {PinNumber}", pinNumber);
}
```

### 3. Thread Safety Requirements

All device implementations must be thread-safe:

```csharp
public class ThreadSafetyPattern
{
    private readonly object _lock = new();
    private volatile bool _disposed;
    
    public bool SomeProperty
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _internalState;
            }
        }
    }
    
    public void SomeMethod()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            // Perform operation
        }
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }
}
```

## Testing Patterns

### 1. Hardware Simulation

```csharp
public class SimulatedGpioController : IGpioController
{
    private readonly Dictionary<int, PinValue> _pinStates = new();
    private readonly Dictionary<int, PinMode> _pinModes = new();
    private readonly Dictionary<int, List<PinChangeEventHandler>> _callbacks = new();
    
    public void OpenPin(int pinNumber, PinMode mode)
    {
        _pinModes[pinNumber] = mode;
        _pinStates[pinNumber] = mode == PinMode.Input ? PinValue.Low : PinValue.Low;
    }
    
    public PinValue Read(int pinNumber)
    {
        return _pinStates.TryGetValue(pinNumber, out var value) ? value : PinValue.Low;
    }
    
    public void Write(int pinNumber, PinValue value)
    {
        if (_pinStates.ContainsKey(pinNumber))
        {
            _pinStates[pinNumber] = value;
            TriggerCallbacks(pinNumber, value);
        }
    }
    
    // Simulation helper methods
    public void SimulatePinChange(int pinNumber, PinValue newValue)
    {
        if (_pinStates.ContainsKey(pinNumber))
        {
            _pinStates[pinNumber] = newValue;
            TriggerCallbacks(pinNumber, newValue);
        }
    }
    
    private void TriggerCallbacks(int pinNumber, PinValue value)
    {
        if (_callbacks.TryGetValue(pinNumber, out var handlers))
        {
            var args = new PinValueChangedEventArgs(PinEventTypes.Rising, pinNumber);
            foreach (var handler in handlers)
            {
                handler(this, args);
            }
        }
    }
}
```

### 2. Unit Test Patterns

```csharp
[TestClass]
public class GpioLimitSwitchTests
{
    private SimulatedGpioController _controller = null!;
    private ILogger<GpioLimitSwitch> _logger = null!;
    
    [TestInitialize]
    public void Setup()
    {
        _controller = new SimulatedGpioController();
        _logger = new NullLogger<GpioLimitSwitch>();
    }
    
    [TestMethod]
    public void Constructor_ValidParameters_InitializesCorrectly()
    {
        // Act
        using var switch = new GpioLimitSwitch(17, GpioLimitSwitch.SwitchPolarity.NormallyOpen, _controller, _logger);
        
        // Assert
        Assert.AreEqual(17, switch.PinNumber);
        Assert.IsTrue(switch.IsInitialized);
        Assert.IsFalse(switch.IsClosed); // Default state
    }
    
    [TestMethod]
    public void StateChange_NormallyOpen_FiresEvent()
    {
        // Arrange
        using var switch = new GpioLimitSwitch(17, GpioLimitSwitch.SwitchPolarity.NormallyOpen, _controller, _logger);
        
        bool eventFired = false;
        switch.StateChanged += (s, e) => eventFired = true;
        
        // Act
        _controller.SimulatePinChange(17, PinValue.Low); // Close switch
        
        // Assert
        Assert.IsTrue(eventFired);
        Assert.IsTrue(switch.IsClosed);
    }
    
    [TestMethod]
    public void Dispose_Multiple_DoesNotThrow()
    {
        // Arrange
        var switch = new GpioLimitSwitch(17, GpioLimitSwitch.SwitchPolarity.NormallyOpen, _controller, _logger);
        
        // Act & Assert
        switch.Dispose();
        switch.Dispose(); // Should not throw
    }
    
    [TestMethod]
    public void StateChanged_RapidChanges_DebouncesProperly()
    {
        // Arrange
        using var switch = new GpioLimitSwitch(17, GpioLimitSwitch.SwitchPolarity.NormallyOpen, _controller, _logger);
        
        int eventCount = 0;
        switch.StateChanged += (s, e) => eventCount++;
        
        // Act - Simulate rapid pin changes
        _controller.SimulatePinChange(17, PinValue.Low);
        _controller.SimulatePinChange(17, PinValue.High);
        _controller.SimulatePinChange(17, PinValue.Low);
        _controller.SimulatePinChange(17, PinValue.High);
        
        // Assert - Should only fire for actual state changes
        Assert.AreEqual(4, eventCount);
    }
}
```

### 3. Integration Test Patterns

```csharp
[TestClass]
public class DeviceIntegrationTests
{
    [TestMethod]
    public async Task BlazorComponent_WithLimitSwitch_UpdatesUI()
    {
        // Arrange
        using var host = await new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IGpioController, SimulatedGpioController>();
            })
            .StartAsync();
        
        var controller = host.Services.GetRequiredService<IGpioController>() as SimulatedGpioController;
        
        // Create component context
        using var ctx = new TestContext();
        ctx.Services.AddSingleton<IGpioController>(controller!);
        
        // Act
        var component = ctx.RenderComponent<RoofControllerComponent>();
        controller!.SimulatePinChange(17, PinValue.Low); // Trigger limit switch
        
        // Assert
        component.WaitForAssertion(() =>
        {
            Assert.IsTrue(component.Markup.Contains("LIMIT REACHED"));
        });
    }
}
```

## Performance Considerations

### 1. Memory Management

```csharp
public class PerformantDevice : IDisposable
{
    private readonly ObjectPool<byte[]> _bufferPool;
    private readonly ConcurrentQueue<DeviceEvent> _eventQueue;
    
    public PerformantDevice()
    {
        _bufferPool = new DefaultObjectPool<byte[]>(new DefaultPooledObjectPolicy<byte[]>());
        _eventQueue = new ConcurrentQueue<DeviceEvent>();
    }
    
    private void ProcessEvents()
    {
        var buffer = _bufferPool.Get();
        try
        {
            // Use buffer for processing
            while (_eventQueue.TryDequeue(out var deviceEvent))
            {
                ProcessEvent(deviceEvent, buffer);
            }
        }
        finally
        {
            _bufferPool.Return(buffer);
        }
    }
}
```

### 2. High-Frequency Operations

```csharp
public class HighFrequencyDevice
{
    private readonly ILogger<HighFrequencyDevice> _logger;
    private int _logThrottleCounter;
    private DateTime _lastLogTime = DateTime.MinValue;
    
    private void OnHighFrequencyEvent()
    {
        // Throttle logging for high-frequency events
        _logThrottleCounter++;
        
        if (DateTime.UtcNow - _lastLogTime > TimeSpan.FromSeconds(5))
        {
            _logger?.LogTrace("High frequency events: {Count} in last 5 seconds", _logThrottleCounter);
            _logThrottleCounter = 0;
            _lastLogTime = DateTime.UtcNow;
        }
    }
}
```

## Deployment Considerations

### 1. Platform Detection

```csharp
public static class DeviceFactory
{
    public static IGpioController CreateGpioController()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Real hardware on Raspberry Pi
            return new GpioController();
        }
        else
        {
            // Simulated controller for development
            return new SimulatedGpioController();
        }
    }
}
```

### 2. Dependency Injection Setup

```csharp
// Program.cs
public static void ConfigureIoTServices(IServiceCollection services, IConfiguration configuration)
{
    // GPIO Controller
    services.AddSingleton<IGpioController>(_ => DeviceFactory.CreateGpioController());
    
    // Device services
    services.AddTransient<RoofLimitSwitchService>();
    services.AddTransient<RoofMotorControlService>();
    
    // Configuration
    services.Configure<IoTDeviceOptions>(configuration.GetSection("IoTDevices"));
}
```

### 3. Configuration

```json
{
  "IoTDevices": {
    "RoofController": {
      "OpenLimitPin": 17,
      "CloseLimitPin": 27,
      "MotorRelayPin": 22,
      "MotorDirectionPin": 23,
      "EnableDebouncing": true,
      "DebounceTimeMs": 50
    },
    "SafetySystem": {
      "EmergencyStopPin": 24,
      "PowerMonitorPin": 25,
      "AlertRelayPin": 26
    }
  }
}
```

## Device Catalog

### Currently Implemented

| Device | Pin Type | Features | Status |
|--------|----------|----------|---------|
| GpioLimitSwitch | Input | Polarity config, debouncing, events | ✅ Complete |
| GpioRelay | Output | State control, feedback | ✅ Complete |

### Planned Devices

| Device | Pin Type | Features | Status |
|--------|----------|----------|---------|
| GpioMotorController | Output | Direction, speed, limits | 🚧 In Progress |
| GpioPWMController | Output | Duty cycle, frequency | 📋 Planned |
| GpioTemperatureSensor | Input | I2C/SPI interface | 📋 Planned |
| GpioRotaryEncoder | Input | Position tracking, events | 📋 Planned |

## Troubleshooting

### Common Issues

1. **Pin Access Denied**
   ```bash
   # Add user to gpio group
   sudo usermod -a -G gpio $USER
   
   # Or run with sudo (not recommended for production)
   sudo dotnet run
   ```

2. **Pin Already in Use**
   ```csharp
   // Check if pin is available before use
   if (_controller.IsPinOpen(pinNumber))
   {
       _logger.LogWarning("Pin {PinNumber} already open, closing first", pinNumber);
       _controller.ClosePin(pinNumber);
   }
   ```

3. **GPIO Not Available**
   ```csharp
   // Graceful fallback to simulation
   try
   {
       return new GpioController();
   }
   catch (PlatformNotSupportedException)
   {
       _logger.LogWarning("GPIO not available, using simulation");
       return new SimulatedGpioController();
   }
   ```

### Debugging Tools

1. **GPIO Test App**
   ```bash
   cd src/HVO.Playground/HVO.GpioTestApp
   dotnet run
   ```

2. **Pin State Monitor**
   ```bash
   # Monitor pin states in real-time
   gpioget gpiochip0 17 27 22 23
   ```

3. **Hardware Validation**
   ```bash
   # Test physical connections
   gpio readall  # On Raspberry Pi
   ```

## Best Practices

### 1. Error Handling

```csharp
public class DeviceErrorHandlingBestPractices
{
    // Always wrap GPIO operations in try-catch
    public bool TryInitializeDevice(int pinNumber)
    {
        try
        {
            _controller.OpenPin(pinNumber, PinMode.Input);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "Permission denied for pin {PinNumber}. Run as root or add user to gpio group.", pinNumber);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Pin {PinNumber} already in use", pinNumber);
            return false;
        }
    }
    
    // Graceful degradation
    public void OperateDevice()
    {
        if (!IsInitialized)
        {
            _logger?.LogWarning("Device not initialized, operation skipped");
            return;
        }
        
        // Proceed with operation
    }
}
```

### 2. Resource Management

```csharp
public class ResourceManagementBestPractices : IDisposable
{
    private bool _disposed;
    
    // Implement IDisposable properly
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
                _pin?.Dispose();
                _controller?.Dispose();
            }
            
            _disposed = true;
        }
    }
    
    // Use 'using' statements for temporary devices
    public void PerformTemporaryOperation()
    {
        using var tempDevice = new GpioRelay(22, _controller);
        tempDevice.SetState(true);
        Thread.Sleep(1000);
        // Device automatically disposed
    }
}
```

### 3. Configuration Validation

```csharp
public class ConfigurationValidation
{
    public static void ValidateIoTConfiguration(IoTDeviceOptions options)
    {
        // Validate pin numbers
        var allPins = new[] { options.OpenLimitPin, options.CloseLimitPin, options.MotorRelayPin };
        var duplicates = allPins.GroupBy(p => p).Where(g => g.Count() > 1);
        
        if (duplicates.Any())
        {
            throw new InvalidOperationException($"Duplicate pins configured: {string.Join(", ", duplicates.Select(g => g.Key))}");
        }
        
        // Validate pin ranges (GPIO 0-27 on most Pi models)
        var invalidPins = allPins.Where(p => p < 0 || p > 27);
        if (invalidPins.Any())
        {
            throw new InvalidOperationException($"Invalid pin numbers: {string.Join(", ", invalidPins)}");
        }
    }
}
```

## Related Documentation

- [Hardware Simulation Improvements](../../guides/hardware-simulation-improvements.md)
- [Raspberry Pi GPIO Pinout Reference](https://pinout.xyz/)
- [.NET IoT Libraries Documentation](https://learn.microsoft.com/en-us/dotnet/iot/)
- [HVO.Iot.Devices API Reference](../../../src/HVO.Iot/README.md)