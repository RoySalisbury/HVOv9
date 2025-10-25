using System;
using System.Device.Gpio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using HVO.Iot.Devices.Tests.TestHelpers;

namespace HVO.Iot.Devices.Tests;

/// <summary>
/// Unit tests for <see cref="MemoryGpioControllerClient"/> to verify Raspberry Pi 5 GPIO behavior simulation.
/// Uses dependency injection to test the <see cref="IGpioControllerClient"/> interface implementation.
/// This demonstrates how to easily switch between in-memory and real hardware implementations.
/// </summary>
[TestClass]
public class MemoryGpioControllerClientTests : IDisposable
{
    private ServiceProvider _serviceProvider = null!;
    private IGpioControllerClient _gpioController = null!;
    private MemoryGpioControllerClient _memoryController = null!;
    private const int ValidPin = 18; // GPIO18 is a standard pin on Pi 5
    private const int InvalidPin = 100; // Invalid pin number
    
    [TestInitialize]
    public void TestInitialize()
    {
        // Use the test helper to configure dependency injection for the memory GPIO client
        _serviceProvider = GpioTestConfiguration.CreateMemoryGpioServiceProvider();
        _gpioController = _serviceProvider.GetRequiredService<IGpioControllerClient>();
        _memoryController = _gpioController as MemoryGpioControllerClient
            ?? throw new InvalidOperationException("Expected MemoryGpioControllerClient but got a different implementation");
    }

    [TestCleanup]
    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    #region Pin Mode Support Tests

    [TestMethod]
    [DataRow(0, PinMode.Input, true)]
    [DataRow(27, PinMode.Input, true)]
    [DataRow(47, PinMode.Input, true)]
    [DataRow(18, PinMode.Output, true)]
    [DataRow(18, PinMode.InputPullUp, true)]
    [DataRow(18, PinMode.InputPullDown, true)]
    [DataRow(100, PinMode.Input, false)] // Invalid pin
    public void IsPinModeSupported_ShouldReturnCorrectSupport(int pinNumber, PinMode mode, bool expectedSupport)
    {
        // Act
        var isSupported = _gpioController.IsPinModeSupported(pinNumber, mode);

        // Assert
    Assert.AreEqual(expectedSupport, isSupported);
    }

    [TestMethod]
    public void IsPinModeSupported_WithInvalidPinMode_ShouldReturnFalse()
    {
        // Act
        var isSupported = _gpioController.IsPinModeSupported(ValidPin, (PinMode)999);

        // Assert
        Assert.IsFalse(isSupported);
    }

    #endregion

    #region Pin Open/Close Tests

    [TestMethod]
    public void IsPinOpen_InitialState_ShouldReturnFalse()
    {
        // Act
        var isOpen = _gpioController.IsPinOpen(ValidPin);

        // Assert
        Assert.IsFalse(isOpen);
    }

    [TestMethod]
    public void IsPinOpen_WithInvalidPin_ShouldReturnFalse()
    {
        // Act
        var isOpen = _gpioController.IsPinOpen(InvalidPin);

        // Assert
        Assert.IsFalse(isOpen);
    }

    [TestMethod]
    public void OpenPin_WithValidPin_ShouldOpenSuccessfully()
    {
        // Act
        _gpioController.OpenPin(ValidPin, PinMode.Output);

        // Assert
        Assert.IsTrue(_gpioController.IsPinOpen(ValidPin));
        Assert.AreEqual(PinMode.Output, _memoryController.GetPinMode(ValidPin));
    }

    [TestMethod]
    public void OpenPin_WithInvalidPin_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _gpioController.OpenPin(InvalidPin, PinMode.Input);
        
        // Assert
        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.Message.Should().Contain("not a valid GPIO pin");
    }

    [TestMethod]
    public void OpenPin_WithUnsupportedMode_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _gpioController.OpenPin(ValidPin, (PinMode)999);
        
        // Assert
        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.Message.Should().Contain("not supported");
    }

    [TestMethod]
    public void OpenPin_WhenAlreadyOpen_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Input);

        // Act
        Action act = () => _gpioController.OpenPin(ValidPin, PinMode.Output);
        
        // Assert
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("already open");
    }

    [TestMethod]
    public void ClosePin_ShouldCloseSuccessfully()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Output);
        Assert.IsTrue(_gpioController.IsPinOpen(ValidPin));

        // Act
        _gpioController.ClosePin(ValidPin);

        // Assert
        Assert.IsFalse(_gpioController.IsPinOpen(ValidPin));
    }

    [TestMethod]
    public void ClosePin_WithInvalidPin_ShouldNotThrow()
    {
        // Act & Assert - Should not throw
        _gpioController.ClosePin(InvalidPin);
    }

    #endregion

    #region Initial Pin Values Tests

    [TestMethod]
    public void OpenPin_WithInputPullUp_ShouldInitializeToHigh()
    {
        // Act
        _gpioController.OpenPin(ValidPin, PinMode.InputPullUp);

        // Assert
        Assert.AreEqual(PinValue.High, _gpioController.Read(ValidPin));
    }

    [TestMethod]
    public void OpenPin_WithInputPullDown_ShouldInitializeToLow()
    {
        // Act
        _gpioController.OpenPin(ValidPin, PinMode.InputPullDown);

        // Assert
        Assert.AreEqual(PinValue.Low, _gpioController.Read(ValidPin));
    }

    [TestMethod]
    public void OpenPin_WithInput_ShouldInitializeToLow()
    {
        // Act
        _gpioController.OpenPin(ValidPin, PinMode.Input);

        // Assert
        Assert.AreEqual(PinValue.Low, _gpioController.Read(ValidPin));
    }

    [TestMethod]
    public void OpenPin_WithOutput_ShouldInitializeToLow()
    {
        // Act
        _gpioController.OpenPin(ValidPin, PinMode.Output);

        // Assert
        Assert.AreEqual(PinValue.Low, _gpioController.Read(ValidPin));
    }

    #endregion

    #region Read/Write Tests

    [TestMethod]
    public void Read_FromClosedPin_ShouldThrowInvalidOperationException()
    {
        // Act
        Action act = () => _gpioController.Read(ValidPin);
        
        // Assert
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("not open");
    }

    [TestMethod]
    public void Read_FromInvalidPin_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _gpioController.Read(InvalidPin);
        
        // Assert
        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.Message.Should().Contain("not a valid GPIO pin");
    }

    [TestMethod]
    public void Write_ToOutputPin_ShouldUpdateValue()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Output);

        // Act
        _gpioController.Write(ValidPin, PinValue.High);

        // Assert
        Assert.AreEqual(PinValue.High, _gpioController.Read(ValidPin));
    }

    [TestMethod]
    public void Write_ToInputPin_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Input);

        // Act
        Action act = () => _gpioController.Write(ValidPin, PinValue.High);
        
        // Assert
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("not configured as output");
    }

    [TestMethod]
    public void Write_ToClosedPin_ShouldThrowInvalidOperationException()
    {
        // Act
        Action act = () => _gpioController.Write(ValidPin, PinValue.High);
        
        // Assert
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("not open");
    }

    [TestMethod]
    public void Write_ToInvalidPin_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _gpioController.Write(InvalidPin, PinValue.High);
        
        // Assert
        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.Message.Should().Contain("not a valid GPIO pin");
    }

    #endregion

    #region Pin Value Change Simulation Tests

    [TestMethod]
    public void SimulatePinValueChange_OnInputPin_ShouldUpdateValue()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Input);

        // Act
        _memoryController.SimulatePinValueChange(ValidPin, PinValue.High);

        // Assert
        Assert.AreEqual(PinValue.High, _gpioController.Read(ValidPin));
    }

    [TestMethod]
    public void SimulatePinValueChange_OnOutputPin_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Output);

        // Act
        Action act = () => _memoryController.SimulatePinValueChange(ValidPin, PinValue.High);
        
        // Assert
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("output pin");
    }

    [TestMethod]
    public void SimulatePinValueChange_OnClosedPin_ShouldThrowInvalidOperationException()
    {
        // Act
        Action act = () => _memoryController.SimulatePinValueChange(ValidPin, PinValue.High);
        
        // Assert
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("not open");
    }

    [TestMethod]
    public void SimulatePinValueChange_OnInvalidPin_ShouldThrowArgumentException()
    {
        // Act
        Action act = () => _memoryController.SimulatePinValueChange(InvalidPin, PinValue.High);
        
        // Assert
        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.Message.Should().Contain("not a valid GPIO pin");
    }

    #endregion

    #region Event Callback Tests

    [TestMethod]
    public void RegisterCallbackForPinValueChangedEvent_ShouldRegisterSuccessfully()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Input);
        var callbackInvoked = false;
        PinChangeEventHandler callback = (sender, args) => callbackInvoked = true;

        // Act
        _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Rising, callback);
        _memoryController.SimulatePinValueChange(ValidPin, PinValue.High);

        // Assert
        Assert.IsTrue(callbackInvoked);
    }

    [TestMethod]
    public void RegisterCallbackForPinValueChangedEvent_WithNullCallback_ShouldThrowArgumentNullException()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Input);

        // Act & Assert
        Action act = () => 
            _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Rising, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void RegisterCallbackForPinValueChangedEvent_OnClosedPin_ShouldThrowInvalidOperationException()
    {
        // Arrange
        PinChangeEventHandler callback = (sender, args) => { };

        // Act
        Action act = () => _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Rising, callback);
        
        // Assert
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("not open");
    }

    [TestMethod]
    public void EventCallback_ShouldTriggerOnRisingEdge()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.InputPullDown); // Starts at Low
        var eventTriggered = false;
        PinEventTypes capturedEventType = PinEventTypes.None;
        int capturedPinNumber = -1;

        PinChangeEventHandler callback = (sender, args) =>
        {
            eventTriggered = true;
            capturedEventType = args.ChangeType;
            capturedPinNumber = args.PinNumber;
        };

        _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Rising, callback);

        // Act
        _memoryController.SimulatePinValueChange(ValidPin, PinValue.High);

        // Assert
        Assert.IsTrue(eventTriggered);
    Assert.AreEqual(PinEventTypes.Rising, capturedEventType);
    Assert.AreEqual(ValidPin, capturedPinNumber);
    }

    [TestMethod]
    public void EventCallback_ShouldTriggerOnFallingEdge()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.InputPullUp); // Starts at High
        var eventTriggered = false;
        PinEventTypes capturedEventType = PinEventTypes.None;

        PinChangeEventHandler callback = (sender, args) =>
        {
            eventTriggered = true;
            capturedEventType = args.ChangeType;
        };

        _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Falling, callback);

        // Act
        _memoryController.SimulatePinValueChange(ValidPin, PinValue.Low);

        // Assert
        Assert.IsTrue(eventTriggered);
    Assert.AreEqual(PinEventTypes.Falling, capturedEventType);
    }

    [TestMethod]
    public void EventCallback_ShouldNotTriggerForWrongEventType()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.InputPullDown); // Starts at Low
        var eventTriggered = false;

        PinChangeEventHandler callback = (sender, args) => eventTriggered = true;
        _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Falling, callback);

        // Act - Trigger rising edge, but callback is registered for falling
        _memoryController.SimulatePinValueChange(ValidPin, PinValue.High);

        // Assert
        Assert.IsFalse(eventTriggered);
    }

    [TestMethod]
    public void UnregisterCallbackForPinValueChangedEvent_ShouldRemoveCallback()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Input);
        var eventTriggered = false;
        PinChangeEventHandler callback = (sender, args) => eventTriggered = true;

        _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Rising, callback);

        // Act
        _gpioController.UnregisterCallbackForPinValueChangedEvent(ValidPin, callback);
        _memoryController.SimulatePinValueChange(ValidPin, PinValue.High);

        // Assert
        Assert.IsFalse(eventTriggered);
    }

    [TestMethod]
    public void UnregisterCallbackForPinValueChangedEvent_WithNullCallback_ShouldNotThrow()
    {
        // Act & Assert - Should not throw
        _gpioController.UnregisterCallbackForPinValueChangedEvent(ValidPin, null!);
    }

    [TestMethod]
    public void UnregisterCallbackForPinValueChangedEvent_WithInvalidPin_ShouldNotThrow()
    {
        // Arrange
        PinChangeEventHandler callback = (sender, args) => { };

        // Act & Assert - Should not throw
        _gpioController.UnregisterCallbackForPinValueChangedEvent(InvalidPin, callback);
    }

    #endregion

    #region Output Pin Event Tests

    [TestMethod]
    public void Write_ToOutputPin_ShouldTriggerEvents()
    {
        // Arrange
        _gpioController.OpenPin(ValidPin, PinMode.Output);
        var risingEventTriggered = false;
        var fallingEventTriggered = false;

        PinChangeEventHandler risingCallback = (sender, args) => risingEventTriggered = true;
        PinChangeEventHandler fallingCallback = (sender, args) => fallingEventTriggered = true;

        _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Rising, risingCallback);
        _gpioController.RegisterCallbackForPinValueChangedEvent(ValidPin, PinEventTypes.Falling, fallingCallback);

        // Act - Rising edge
        _gpioController.Write(ValidPin, PinValue.High);

        // Assert
        Assert.IsTrue(risingEventTriggered);
        Assert.IsFalse(fallingEventTriggered);

        // Reset flags
        risingEventTriggered = false;
        fallingEventTriggered = false;

        // Act - Falling edge
        _gpioController.Write(ValidPin, PinValue.Low);

        // Assert
        Assert.IsFalse(risingEventTriggered);
        Assert.IsTrue(fallingEventTriggered);
    }

    #endregion

    #region Disposal Tests

    [TestMethod]
    public void Dispose_ShouldCloseAllPins()
    {
        // Arrange
        _gpioController.OpenPin(18, PinMode.Output);
        _gpioController.OpenPin(19, PinMode.Input);
        Assert.IsTrue(_gpioController.IsPinOpen(18));
        Assert.IsTrue(_gpioController.IsPinOpen(19));

        // Act
        _memoryController.Dispose();

    // Assert - After disposal, operations should throw ObjectDisposedException
    // This verifies that dispose properly cleaned up
    Action act1 = () => _gpioController.IsPinOpen(18);
    act1.Should().Throw<ObjectDisposedException>();
    Action act2 = () => _gpioController.IsPinOpen(19);
    act2.Should().Throw<ObjectDisposedException>();
    }

    [TestMethod]
    public void AfterDispose_AllOperations_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _memoryController.Dispose();

        // Act & Assert
        Action a1 = () => _gpioController.IsPinModeSupported(ValidPin, PinMode.Input);
        a1.Should().Throw<ObjectDisposedException>();

        Action a2 = () => _gpioController.IsPinOpen(ValidPin);
        a2.Should().Throw<ObjectDisposedException>();

        Action a3 = () => _gpioController.OpenPin(ValidPin, PinMode.Input);
        a3.Should().Throw<ObjectDisposedException>();

        Action a4 = () => _gpioController.ClosePin(ValidPin);
        a4.Should().Throw<ObjectDisposedException>();

        Action a5 = () => _gpioController.Read(ValidPin);
        a5.Should().Throw<ObjectDisposedException>();

        Action a6 = () => _gpioController.Write(ValidPin, PinValue.High);
        a6.Should().Throw<ObjectDisposedException>();

        Action a7 = () => _memoryController.SimulatePinValueChange(ValidPin, PinValue.High);
        a7.Should().Throw<ObjectDisposedException>();
    }

    #endregion

    #region Raspberry Pi 5 Specific Pin Tests

    [TestMethod]
    [DataRow(0)]   // GPIO 0
    [DataRow(1)]   // GPIO 1
    [DataRow(27)]  // GPIO 27 (last standard pin)
    [DataRow(47)]  // GPIO 47 (last available pin on BCM2712)
    public void ValidRaspberryPi5Pins_ShouldBeSupported(int pinNumber)
    {
        // Act & Assert
        Assert.IsTrue(_gpioController.IsPinModeSupported(pinNumber, PinMode.Input));
        Assert.IsTrue(_gpioController.IsPinModeSupported(pinNumber, PinMode.Output));
        Assert.IsTrue(_gpioController.IsPinModeSupported(pinNumber, PinMode.InputPullUp));
        Assert.IsTrue(_gpioController.IsPinModeSupported(pinNumber, PinMode.InputPullDown));
    }

    [TestMethod]
    [DataRow(48)]  // Beyond BCM2712 GPIO range
    [DataRow(100)] // Clearly invalid
    [DataRow(-1)]  // Negative pin number
    public void InvalidRaspberryPi5Pins_ShouldNotBeSupported(int pinNumber)
    {
        // Act & Assert
        Assert.IsFalse(_gpioController.IsPinModeSupported(pinNumber, PinMode.Input));
    }

    #endregion
}
