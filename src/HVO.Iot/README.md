# HVO.Iot - Hardware Device Abstractions

[![IoT Domain CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/iot.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/iot.yml)

Domain containing hardware device abstractions for GPIO control, sensors, and IoT peripherals used throughout the HVOv9 observatory automation suite.

## 📦 Domain Overview

The **HVO.Iot** domain provides reusable, testable abstractions for physical hardware devices, enabling:
- **Platform-agnostic code** - Same APIs work on Raspberry Pi, desktop, CI/CD
- **Hardware simulation** - Test hardware logic without physical devices
- **Type-safe GPIO** - Strongly-typed device classes vs. raw pin manipulation
- **Future NuGet packaging** - Potential standalone library for .NET IoT projects

## 📁 Projects in This Domain

### HVO.Iot.Devices
Core library of hardware device implementations:
- **Limit switches** (normally-open, normally-closed)
- **Relays and motor controllers**
- **Environmental sensors** (future)
- **Camera trigger circuits** (future)

### HVO.Iot.Devices.Tests
Comprehensive unit tests with hardware simulation:
- Verify device state machines
- Test edge cases (debouncing, race conditions)
- Validate thread-safety
- Mock GPIO pins for CI/CD

## 🔑 Key Devices

### GpioLimitSwitch
Debounced limit switch with configurable polarity:

```csharp
// Normally-open switch (circuit completes when pressed)
using var openSwitch = new GpioLimitSwitch(
    pinNumber: 17,
    polarity: GpioLimitSwitch.SwitchPolarity.NormallyOpen,
    controller: controller);

openSwitch.StateChanged += (sender, e) =>
{
    Console.WriteLine($"Switch {(e.IsClosed ? "CLOSED" : "OPEN")}");
};

// Check current state
if (openSwitch.IsClosed)
{
    Console.WriteLine("Limit reached!");
}
```

### GpioRelay
Relay control with automatic state management:

```csharp
using var relay = new GpioRelay(pinNumber: 22, controller: controller);

// Activate relay
relay.SetState(true);
await Task.Delay(5000);

// Deactivate relay
relay.SetState(false);
```

## 🎓 Usage Patterns

### Hardware Simulation for Testing

```csharp
// Create simulated GPIO controller
var controller = new SimulatedGpioController();

// Create device with simulated hardware
var switch = new GpioLimitSwitch(17, GpioLimitSwitch.SwitchPolarity.NormallyOpen, controller);

// Simulate hardware events
controller.SimulatePinChange(17, PinValue.High);

// Assert device responded
Assert.IsTrue(switch.IsClosed);
```

### Integration with Blazor Components

```csharp
@inject IGpioController GpioController
@implements IDisposable

<div>
    Roof Open Limit: @(_openLimit?.IsClosed == true ? "REACHED" : "Not Reached")
</div>

@code {
    private GpioLimitSwitch? _openLimit;
    
    protected override void OnInitialized()
    {
        _openLimit = new GpioLimitSwitch(17, GpioLimitSwitch.SwitchPolarity.NormallyOpen, GpioController);
        _openLimit.StateChanged += OnLimitStateChanged;
    }
    
    private async void OnLimitStateChanged(object? sender, EventArgs e)
    {
        await InvokeAsync(StateHasChanged);
    }
    
    public void Dispose()
    {
        _openLimit?.Dispose();
    }
}
```

### Dependency Injection Setup

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

## 🧪 Testing

### Run Domain Tests
```bash
cd src/HVO.Iot
dotnet test
```

### Test Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

### Build Domain Solution
```bash
cd src/HVO.Iot
dotnet build HVO.Iot.sln
```

## 🔗 Dependencies

### HVO.Iot.Devices
- `System.Device.Gpio` - .NET GPIO APIs
- `HVO` - Core library (Result<T>, Option<T>)
- `Microsoft.Extensions.Logging.Abstractions` - Structured logging

### HVO.Iot.Devices.Tests
- `MSTest.TestFramework` - Test runner
- `coverlet.collector` - Code coverage

## 📚 Used By

- `HVO.RoofControllerV4.RPi` - Roof motor control, limit switches
- `HVO.SkyMonitorV5.RPi` - Camera trigger, environmental sensors (future)
- `HVO.WebSite.v9` - Hardware status display

## 🎨 Design Patterns

### Event-Driven Architecture
Devices raise events for state changes:
```csharp
limitSwitch.StateChanged += OnStateChanged;
relay.StateChanged += OnRelayToggled;
```

### IDisposable Implementation
All devices properly release GPIO resources:
```csharp
public void Dispose()
{
    _pin?.Dispose();
    _controller?.Dispose();
}
```

### Thread-Safe Operations
All device methods are thread-safe with proper locking:
```csharp
private readonly object _lock = new();

public bool IsClosed
{
    get { lock (_lock) { return _isClosed; } }
}
```

## 🔄 Future Enhancements

- [ ] Package HVO.Iot.Devices as standalone NuGet
- [ ] Add I2C sensor abstractions (temperature, humidity, pressure)
- [ ] Add SPI device support (ADCs, DACs)
- [ ] Create visual device simulator UI
- [ ] Add PWM motor control abstractions
- [ ] Support remote GPIO over network (pigpio)

## 📖 Related Documentation

- [System.Device.Gpio Documentation](https://learn.microsoft.com/en-us/dotnet/iot/intro)
- [Raspberry Pi GPIO Pinout](https://pinout.xyz/)
- [HVO Hardware Simulation Guide](../../docs/guides/hardware-simulation-improvements.md)
- [IoT Device Development Guide](../../docs/projects/iot-devices/README.md)
