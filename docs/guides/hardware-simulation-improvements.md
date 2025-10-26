# Hardware Simulation Improvements Guide

This guide provides strategies for improving hardware device simulation and testing throughout the HVOv9 platform.

## Overview

HVOv9 uses hardware abstractions to enable development and testing without physical hardware. This guide outlines best practices for creating reliable simulations and comprehensive testing strategies.

## GPIO Simulation Patterns

### Simulated GPIO Controller

```csharp
public class SimulatedGpioController : IGpioController
{
    private readonly Dictionary<int, PinValue> _pinStates = new();
    private readonly Dictionary<int, PinMode> _pinModes = new();
    
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
        if (_pinModes.TryGetValue(pinNumber, out var mode) && mode == PinMode.Output)
        {
            _pinStates[pinNumber] = value;
            PinValueChanged?.Invoke(this, new PinValueChangedEventArgs(pinNumber, value));
        }
    }
    
    // Simulation helpers
    public void SimulatePinChange(int pinNumber, PinValue newValue)
    {
        _pinStates[pinNumber] = newValue;
        PinValueChanged?.Invoke(this, new PinValueChangedEventArgs(pinNumber, newValue));
    }
    
    public event EventHandler<PinValueChangedEventArgs>? PinValueChanged;
}
```

## Testing Strategies

### Hardware Device Testing

```csharp
[TestMethod]
public void LimitSwitch_StateChange_FiresEvent()
{
    // Arrange
    var controller = new SimulatedGpioController();
    var switch = new GpioLimitSwitch(17, GpioLimitSwitch.SwitchPolarity.NormallyOpen, controller);
    
    bool eventFired = false;
    switch.StateChanged += (s, e) => eventFired = true;
    
    // Act
    controller.SimulatePinChange(17, PinValue.High);
    
    // Assert
    Assert.IsTrue(eventFired);
    Assert.IsTrue(switch.IsClosed);
}
```

### Integration Testing

Use dependency injection to swap real hardware for simulations:

```csharp
// Program.cs
builder.Services.AddSingleton<IGpioController>(sp =>
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        return new GpioController();  // Real hardware on RPi
    }
    else
    {
        return new SimulatedGpioController();  // Simulated on dev machines
    }
});
```

## Debugging Hardware Issues

### GPIO Testing Utility

Use `HVO.GpioTestApp` for interactive hardware debugging:

```bash
cd src/HVO.Playground/HVO.GpioTestApp
dotnet run

# Select "Monitor all pins"
# Enter pin numbers: 17,27,22
# Physically trigger devices and verify state changes
```

### Logging Best Practices

```csharp
public class GpioLimitSwitch : IDisposable
{
    private readonly ILogger<GpioLimitSwitch>? _logger;
    
    private void OnPinValueChanged(object sender, PinValueChangedEventArgs e)
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
```

## Performance Considerations

### High-Frequency Operations

For operations that run frequently (like timer callbacks), use appropriate log levels:

```csharp
// Use Trace for high-frequency operations
_logger?.LogTrace("Timer callback executed, current state: {State}", _currentState);

// Use Debug for state changes
_logger?.LogDebug("Device state changed from {OldState} to {NewState}", oldState, newState);
```

### Memory Management

Ensure proper disposal of hardware resources:

```csharp
public void Dispose()
{
    _timer?.Dispose();
    _pin?.Dispose();
    _controller?.Dispose();
}
```

## CI/CD Integration

### GitHub Actions Testing

Hardware simulations enable full testing in CI/CD:

```yaml
- name: Run Hardware Tests
  run: dotnet test src/HVO.Iot.Devices.Tests --logger trx --results-directory TestResults/
  env:
    HVO_HARDWARE_SIMULATION: true
```

### Cross-Platform Validation

Test on multiple platforms to ensure simulation consistency:

```yaml
strategy:
  matrix:
    os: [ubuntu-latest, windows-latest, macos-latest]
    dotnet-version: ['9.0.x']
```

## Related Documentation

- [GPIO Testing with HVO.GpioTestApp](../projects/playground/gpio-testing.md)
- [Hardware Device Development](../projects/iot-devices/README.md)
- [Blazor Component Hardware Integration](./blazor-component-best-practices.md)