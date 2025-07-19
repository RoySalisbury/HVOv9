using System;
using System.Device.Gpio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using HVO.Iot.Devices.Tests.TestHelpers;

namespace HVO.Iot.Devices.Tests.Integration;

/// <summary>
/// Integration tests that can run against real GPIO hardware on Raspberry Pi.
/// These tests are designed to work with both mock and real GPIO controllers
/// by using dependency injection configuration.
/// 
/// To run against real hardware:
/// 1. Deploy to Raspberry Pi 5
/// 2. Change UseRealHardware to true
/// 3. Ensure proper GPIO pin connections
/// </summary>
[TestClass]
public class GpioHardwareIntegrationTests : IDisposable
{
    private ServiceProvider _serviceProvider = null!;
    private IGpioController _gpioController = null!;
    
    // Configuration: Set to true to test against real Raspberry Pi GPIO hardware
    // Set to false to test against mock implementation
    // Default to false for safety - only enable real hardware on supported platforms
    private static readonly bool UseRealHardware = Environment.GetEnvironmentVariable("USE_REAL_GPIO") == "true" && 
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
    
    private const int TestOutputPin = 18; // GPIO18 for LED or similar output device
    private const int TestInputPin = 24;  // GPIO24 for button or similar input device
    
    [TestInitialize]
    public void TestInitialize()
    {
        // Configure dependency injection based on hardware availability
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

    [TestCleanup]
    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    [TestMethod]
    public void BasicGpioOperations_ShouldWorkWithBothMockAndRealHardware()
    {
        // Arrange
        const int pin = TestOutputPin;
        
        // Act & Assert - Basic pin operations
        Assert.IsTrue(_gpioController.IsPinModeSupported(pin, PinMode.Output));
        
        _gpioController.OpenPin(pin, PinMode.Output);
        Assert.IsTrue(_gpioController.IsPinOpen(pin));
        
        // Test writing values
        _gpioController.Write(pin, PinValue.High);
        _gpioController.Write(pin, PinValue.Low);
        
        _gpioController.ClosePin(pin);
        Assert.IsFalse(_gpioController.IsPinOpen(pin));
    }

    [TestMethod]
    public void InputPinWithPullUp_ShouldReadHighByDefault()
    {
        // Arrange
        const int pin = TestInputPin;
        
        // Act
        _gpioController.OpenPin(pin, PinMode.InputPullUp);
        var value = _gpioController.Read(pin);
        
        // Assert
        Assert.AreEqual(value, PinValue.High);
        
        // Cleanup
        _gpioController.ClosePin(pin);
    }

    [TestMethod]
    public void InputPinWithPullDown_ShouldReadLowByDefault()
    {
        // Arrange
        const int pin = TestInputPin;
        
        // Act
        _gpioController.OpenPin(pin, PinMode.InputPullDown);
        var value = _gpioController.Read(pin);
        
        // Assert
        Assert.AreEqual(value, PinValue.Low);
        
        // Cleanup
        _gpioController.ClosePin(pin);
    }

    [TestMethod]
    public void PinEventRegistration_ShouldNotThrowExceptions()
    {
        // Arrange
        const int pin = TestInputPin;
        var eventTriggered = false;
        
        void EventHandler(object sender, PinValueChangedEventArgs e)
        {
            eventTriggered = true;
        }
        
        // Act
        _gpioController.OpenPin(pin, PinMode.Input);
        _gpioController.RegisterCallbackForPinValueChangedEvent(pin, PinEventTypes.Rising, EventHandler);
        
        // For mock controller, we can simulate an event
        if (!UseRealHardware && _gpioController is MockGpioController mockController)
        {
            mockController.SimulatePinValueChange(pin, PinValue.High);
            Assert.IsTrue(eventTriggered, "Event should have been triggered in mock mode");
        }
        
        // Cleanup
        _gpioController.UnregisterCallbackForPinValueChangedEvent(pin, EventHandler);
        _gpioController.ClosePin(pin);
    }
}
