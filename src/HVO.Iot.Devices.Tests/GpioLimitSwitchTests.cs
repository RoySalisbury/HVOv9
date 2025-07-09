using System;
using System.Device.Gpio;
using System.Threading.Tasks;
using HVO.Iot.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace HVO.Iot.Devices.Tests
{
    [TestClass]
    public class GpioLimitSwitchTests
    {
        private Mock<GpioController> _mockController = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockController = new Mock<GpioController>();

            // Setup IsPinModeSupported to always return true for tests
            _mockController.Setup(c => c.IsPinModeSupported(It.IsAny<int>(), It.IsAny<PinMode>())).Returns(true);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Constructor_ShouldThrow_WhenPinNumberInvalid()
        {
            var _ = new GpioLimitSwitch(_mockController.Object, 0);
        }

        [TestMethod]
        public void PinStateChanged_ShouldRaiseEvent_WhenDebouncePassed()
        {
            var pin = 5;
            var debounce = TimeSpan.Zero; // no debounce to make testing easier
            var limitSwitch = new GpioLimitSwitch(_mockController.Object, pin, debounceTime: debounce);

            LimitSwitchTriggeredEventArgs? eventArgs = null;
            limitSwitch.LimitSwitchTriggered += (s, e) => eventArgs = e;

            // Simulate a pin change event
            var pinEventArgs = new PinValueChangedEventArgs(PinEventTypes.Falling, pin);
            var method = typeof(GpioLimitSwitch).GetMethod("PinStateChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            // Call PinStateChanged via reflection to simulate event (since method is private)
            method.Invoke(limitSwitch, new object[] { null!, pinEventArgs });

            Assert.IsNotNull(eventArgs);
            Assert.AreEqual(pin, eventArgs.PinNumber);
            Assert.AreEqual(PinEventTypes.Falling, eventArgs.ChangeType);
        }

        [TestMethod]
        public void PinStateChanged_ShouldNotRaiseEvent_WhenDebounceNotPassed()
        {
            var pin = 5;
            var debounce = TimeSpan.FromSeconds(1);
            var limitSwitch = new GpioLimitSwitch(_mockController.Object, pin, debounceTime: debounce);

            LimitSwitchTriggeredEventArgs? eventArgs = null;
            limitSwitch.LimitSwitchTriggered += (s, e) => eventArgs = e;

            var method = typeof(GpioLimitSwitch).GetMethod("PinStateChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            // First event (should fire)
            method.Invoke(limitSwitch, new object[] { null!, new PinValueChangedEventArgs(PinEventTypes.Falling, pin) });
            Assert.IsNotNull(eventArgs);

            eventArgs = null;

            // Immediately trigger second event (should NOT fire due to debounce)
            method.Invoke(limitSwitch, new object[] { null!, new PinValueChangedEventArgs(PinEventTypes.Rising, pin) });
            Assert.IsNull(eventArgs);
        }
    }
}
