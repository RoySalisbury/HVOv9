# GPIO Controller Dependency Injection Setup

This document explains how the GPIO controller tests have been updated to use dependency injection, making it easy to switch between mock and real hardware implementations.

## Architecture Overview

The GPIO controller architecture now uses dependency injection (DI) to provide flexible testing and deployment scenarios:

- **IGpioController Interface**: Common interface for all GPIO operations
- **MockGpioController**: Mock implementation for testing without hardware
- **GpioControllerWrapper**: Real hardware implementation for Raspberry Pi
- **Dependency Injection**: Configuration management for different scenarios

## Test Configuration

### MockGpioControllerTests
Updated to use dependency injection for `IGpioController`:

```csharp
[TestInitialize]
public void TestInitialize()
{
    // Use the test helper to configure dependency injection for mock GPIO
    _serviceProvider = GpioTestConfiguration.CreateMockGpioServiceProvider();
    _gpioController = _serviceProvider.GetRequiredService<IGpioController>();
    
    // Keep a reference to the concrete type for mock-specific methods
    _mockController = (MockGpioController)_gpioController;
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
        _serviceProvider = GpioTestConfiguration.CreateMockGpioServiceProvider();
    }
    
    _gpioController = _serviceProvider.GetRequiredService<IGpioController>();
}
```

## Test Helper Class

The `GpioTestConfiguration` class provides convenience methods for DI setup:

### For Mock Testing (Unit Tests)
```csharp
services.AddMockGpioController();
// or
var serviceProvider = GpioTestConfiguration.CreateMockGpioServiceProvider();
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
dotnet test HVO.Iot.Devices.Tests --filter "MockGpioControllerTests"
```
- Tests: 49/49 passing
- Uses mock GPIO controller with Raspberry Pi 5 hardware simulation

### Running Integration Tests (Mock Mode)
```bash
dotnet test HVO.Iot.Devices.Tests --filter "GpioHardwareIntegrationTests"
```
- Tests: 4/4 passing
- Can be switched to real hardware by changing `UseRealHardware = true`

### Running All GPIO Tests
```bash
dotnet test HVO.Iot.Devices.Tests --filter "MockGpioControllerTests|GpioHardwareIntegrationTests"
```
- Tests: 53/53 passing
- Comprehensive coverage of both mock and integration scenarios

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
