using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO.Iot.Devices;
using HVO.Iot.Devices.Implementation;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace HVO.Iot.Devices.Tests
{
    [TestClass]
    public class BlinkingFunctionalityTests
    {
    private GpioButtonWithLed? _buttonWithLed;
    private MemoryGpioControllerClient? _memoryGpioController;

        [TestInitialize]
        public void Setup()
        {
            _memoryGpioController = new MemoryGpioControllerClient();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _buttonWithLed?.Dispose();
            _memoryGpioController?.Dispose();
        }

        [TestMethod]
        public void StartBlinking_WithValidFrequency_SetsIsBlinkingTrue()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act
            _buttonWithLed.StartBlinking(1.0); // 1 Hz

            // Assert
            Assert.IsTrue(_buttonWithLed.IsBlinking, "IsBlinking should be true after StartBlinking");
            Assert.AreEqual(PushButtonLedState.Blinking, _buttonWithLed.LedState, "LedState should be Blinking");
        }

        [TestMethod]
        public void StopBlinking_AfterStartBlinking_SetsIsBlinkingFalse()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.StartBlinking(1.0);

            // Act
            _buttonWithLed.StopBlinking();

            // Assert
            Assert.IsFalse(_buttonWithLed.IsBlinking, "IsBlinking should be false after StopBlinking");
        }

        [TestMethod]
        public void SetBlinkFrequency_WithValidFrequency_UpdatesFrequency()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.StartBlinking(1.0);

            // Act
            _buttonWithLed.SetBlinkFrequency(2.0);

            // Assert
            Assert.IsTrue(_buttonWithLed.IsBlinking, "Should still be blinking after frequency change");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void StartBlinking_WithInvalidFrequency_ThrowsException()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act & Assert
            _buttonWithLed.StartBlinking(15.0); // Above 10 Hz limit
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void StartBlinking_WithZeroFrequency_ThrowsException()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act & Assert
            _buttonWithLed.StartBlinking(0.0);
        }

        private GpioButtonWithLed CreateButtonWithLed(int buttonPin = 18, int? ledPin = 24)
        {
            return new GpioButtonWithLed(
                buttonPin: buttonPin,
                ledPin: ledPin,
                doublePress: TimeSpan.FromMilliseconds(600), // 3x debounce time
                holding: TimeSpan.FromSeconds(2),
                isPullUp: true,
                hasExternalResistor: false,
                gpioController: _memoryGpioController!,
                debounceTime: TimeSpan.FromMilliseconds(200));
        }
    }
}
