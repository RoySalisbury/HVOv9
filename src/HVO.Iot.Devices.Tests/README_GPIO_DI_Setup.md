# GPIO Controller Dependency Injection Setup

This document explains how the GPIO controller tests use dependency injection, making it easy to switch between the in-memory simulator and real hardware implementations.

## Architecture Overview

The GPIO controller architecture now uses dependency injection (DI) to provide flexible testing and deployment scenarios:

- **IGpioControllerClient Interface**: Common interface for all GPIO operations
- **MemoryGpioControllerClient**: In-memory Raspberry Pi 5 simulator for tests
- **GpioControllerClient**: Real hardware implementation for Raspberry Pi
- **Dependency Injection**: Configuration management for different scenarios

## Test Configuration

### MemoryGpioControllerClientTests
Updated to use dependency injection for `IGpioControllerClient`:

```csharp
[TestInitialize]
public void TestInitialize()
{
    // Use the test helper to configure dependency injection for the in-memory client
    _serviceProvider = GpioTestConfiguration.CreateMemoryGpioServiceProvider();
    _gpioController = _serviceProvider.GetRequiredService<IGpioControllerClient>();

    // Keep a reference to the concrete type for simulation helpers
    _memoryController = (MemoryGpioControllerClient)_gpioController;
}
```

### GpioHardwareIntegrationTests
Integration tests that can switch between mock and real hardware:

```csharp
// Configuration: Set to true to test against real Raspberry Pi GPIO hardware
private const bool UseRealHardware = false;

[TestInitialize]
public void TestInitialize()
{
    if (UseRealHardware)
    {
        _serviceProvider = GpioTestConfiguration.CreateRealGpioServiceProvider();
    }
    else
    {
        _serviceProvider = GpioTestConfiguration.CreateMemoryGpioServiceProvider();

    _gpioController = _serviceProvider.GetRequiredService<IGpioControllerClient>();
    _gpioController = _serviceProvider.GetRequiredService<IGpioController>();
}
```

## Test Helper Class

The `GpioTestConfiguration` class provides convenience methods for DI setup:

### For Memory (Unit Tests)
```csharp
services.AddMemoryGpioControllerClient();
// or
var serviceProvider = GpioTestConfiguration.CreateMemoryGpioServiceProvider();
```

### For Real Hardware Testing (Integration Tests)
```csharp
services.AddRealGpioController();
// or
var serviceProvider = GpioTestConfiguration.CreateRealGpioServiceProvider();
```

## Benefits

1. **Easy Hardware Switching**: Change one configuration flag to switch between mock and real hardware
2. **Testability**: All tests work with both implementations through the interface
3. **Production Ready**: Same DI pattern used in production code
4. **Maintainability**: Clear separation of concerns and consistent patterns

## Usage Examples

### Running Unit Tests (Mock Hardware)
```bash
dotnet test HVO.Iot.Devices.Tests --filter "MemoryGpioControllerClientTests"
```
- Exercises the in-memory GPIO controller client simulator

### Running Integration Tests (Mock Mode)
```bash
dotnet test HVO.Iot.Devices.Tests --filter "GpioHardwareIntegrationTests"
```
- Tests: 4/4 passing
- Can be switched to real hardware by changing `UseRealHardware = true`

### Running All GPIO Tests
```bash
dotnet test HVO.Iot.Devices.Tests --filter "MemoryGpioControllerClientTests|GpioHardwareIntegrationTests"
```
- Comprehensive coverage of both simulated and integration scenarios

## Migration Path

To deploy tests on actual Raspberry Pi 5 hardware:

1. Deploy test project to Raspberry Pi 5
2. In `GpioHardwareIntegrationTests.cs`, change `UseRealHardware = true`
3. Ensure proper GPIO pin connections for test pins 18 and 24
4. Run tests: `dotnet test HVO.Iot.Devices.Tests`

The same test code will now run against real GPIO hardware while maintaining all validation logic.

## Dependencies

Added to test project:
- `Microsoft.Extensions.DependencyInjection` for DI container support
- Existing GPIO abstraction interfaces from `HVO.Iot.Devices.Abstractions`

This setup provides a robust foundation for testing GPIO functionality across different environments while maintaining code quality and hardware accuracy.
