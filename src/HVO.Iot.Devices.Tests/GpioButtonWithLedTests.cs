using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Device.Gpio;
using System.Threading;
using System.Threading.Tasks;
using HVO.Iot.Devices;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using HVO.Iot.Devices.Tests.TestHelpers;

namespace HVO.Iot.Devices.Tests
{
    [TestClass]
    public class GpioButtonWithLedTests : IDisposable
    {
        private ServiceProvider? _serviceProvider;
        private IGpioController? _gpioController;
        private MockGpioController? _mockGpioController;
        private ILogger<GpioButtonWithLed>? _logger;
        private GpioButtonWithLed? _buttonWithLed;
        private const int ButtonPin = 18;
        private const int LedPin = 24;
        private const int TestPin = ButtonPin; // For readability in tests
        private const int TestLedPin = LedPin; // For readability in tests

        // Configuration: Set to true to test against real Raspberry Pi GPIO hardware
        // Set to false to test against mock implementation
        // Default to false for safety - only enable real hardware on supported platforms
        private static readonly bool UseRealHardware = Environment.GetEnvironmentVariable("USE_REAL_GPIO") == "true" && 
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);

        [TestInitialize]
        public void Setup()
        {
            // Configure dependency injection based on hardware availability
            if (UseRealHardware)
            {
                _serviceProvider = GpioTestConfiguration.CreateRealGpioServiceProvider();
                _gpioController = _serviceProvider.GetRequiredService<IGpioController>();
                _logger = _serviceProvider.GetRequiredService<ILogger<GpioButtonWithLed>>();
            }
            else
            {
                _serviceProvider = GpioTestConfiguration.CreateMockGpioServiceProvider();
                _gpioController = _serviceProvider.GetRequiredService<IGpioController>();
                _logger = _serviceProvider.GetRequiredService<ILogger<GpioButtonWithLed>>();
                
                // Get reference to the mock controller for direct testing
                // Since we're now using GpioControllerWrapper, we need to access the underlying controller
                if (_gpioController is GpioControllerWrapper wrapper)
                {
                    _mockGpioController = wrapper.UnderlyingController as MockGpioController;
                }
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                _buttonWithLed?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
            _buttonWithLed = null;
            _serviceProvider?.Dispose();
            _gpioController = null;
            _mockGpioController = null;
            _logger = null;
        }

        /// <summary>
        /// Implementation of IDisposable.Dispose()
        /// </summary>
        public void Dispose()
        {
            Cleanup();
        }

        /// <summary>
        /// Creates a GpioButtonWithLed instance for testing
        /// </summary>
        private GpioButtonWithLed CreateButtonWithLed(int buttonPin = TestPin, int? ledPin = TestLedPin, 
            TimeSpan? doublePress = null, TimeSpan? holding = null, bool isPullUp = true, bool hasExternalResistor = false,
            TimeSpan? debounceTime = null)
        {
            return new GpioButtonWithLed(
                buttonPin: buttonPin,
                ledPin: ledPin,
                doublePress: doublePress ?? TimeSpan.FromMilliseconds(300),
                holding: holding ?? TimeSpan.FromSeconds(2),
                isPullUp: isPullUp,
                hasExternalResistor: hasExternalResistor,
                gpioController: _gpioController!,
                debounceTime: debounceTime ?? TimeSpan.FromMilliseconds(50));
        }

        /// <summary>
        /// Simulates a button press by changing the pin value
        /// </summary>
        private void SimulateButtonPress()
        {
            if (!UseRealHardware && _mockGpioController != null)
            {
                _mockGpioController.SimulatePinValueChange(TestPin, PinValue.Low);
            }
        }

        /// <summary>
        /// Simulates a button release by changing the pin value
        /// </summary>
        private void SimulateButtonRelease()
        {
            if (!UseRealHardware && _mockGpioController != null)
            {
                _mockGpioController.SimulatePinValueChange(TestPin, PinValue.High);
            }
        }

        /// <summary>
        /// Simulates a complete button press and release sequence
        /// </summary>
        private void SimulateButtonPressAndRelease(int pressDelayMs = 100, int releaseDelayMs = 100)
        {
            SimulateButtonPress();
            if (pressDelayMs > 0) Thread.Sleep(pressDelayMs);
            SimulateButtonRelease();
            if (releaseDelayMs > 0) Thread.Sleep(releaseDelayMs);
        }

        /// <summary>
        /// Verifies that a pin is configured with the expected mode
        /// </summary>
        private void VerifyPinMode(int pinNumber, PinMode expectedMode)
        {
            if (!UseRealHardware && _mockGpioController != null)
            {
                var actualMode = _mockGpioController.GetPinMode(pinNumber);
                Assert.AreEqual(expectedMode, actualMode, $"Pin {pinNumber} should be configured as {expectedMode} but was {actualMode}");
            }
        }

        #region Initialization Tests

        [TestMethod]
        public void Constructor_WithValidParameters_InitializesCorrectly()
        {
            // Arrange & Act
            _buttonWithLed = CreateButtonWithLed();

            // Assert
            Assert.IsNotNull(_buttonWithLed);
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);
            
            // Verify pins are configured correctly
            if (!UseRealHardware)
            {
                VerifyPinMode(TestPin, PinMode.InputPullUp);
                VerifyPinMode(TestLedPin, PinMode.Output);
            }
        }

        [TestMethod]
        public void Constructor_WithPullDown_ConfiguresCorrectly()
        {
            // Arrange & Act
            _buttonWithLed = CreateButtonWithLed(isPullUp: false);

            // Assert
            Assert.IsNotNull(_buttonWithLed);
            
            // Verify pin is configured with pull-down
            if (!UseRealHardware)
            {
                VerifyPinMode(TestPin, PinMode.InputPullDown);
            }
        }

        [TestMethod]
        public void Constructor_WithExternalResistor_ConfiguresCorrectly()
        {
            // Arrange & Act
            _buttonWithLed = CreateButtonWithLed(hasExternalResistor: true);

            // Assert
            Assert.IsNotNull(_buttonWithLed);
            
            // Verify pin is configured as simple input when external resistor is used
            if (!UseRealHardware)
            {
                VerifyPinMode(TestPin, PinMode.Input);
            }
        }

        [TestMethod]
        public void Constructor_WithSameButtonAndLedPin_ThrowsArgumentException()
        {
            // Arrange & Act & Assert
            Assert.ThrowsException<ArgumentException>(() =>
                CreateButtonWithLed(buttonPin: TestPin, ledPin: TestPin));
        }

        [TestMethod]
        public void Constructor_WithNullLedPin_InitializesWithoutLed()
        {
            // Arrange & Act
            _buttonWithLed = CreateButtonWithLed(ledPin: null);

            // Assert
            Assert.IsNotNull(_buttonWithLed);
            Assert.AreEqual(PushButtonLedState.NotUsed, _buttonWithLed.LedState);
        }

        #endregion

        #region Button Press Event Tests

        [TestMethod]
        public void ButtonDown_WhenPressed_FiresEvent()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            bool eventFired = false;
            _buttonWithLed.ButtonDown += (sender, args) => eventFired = true;

            // Act
            SimulateButtonPressAndRelease();
            Thread.Sleep(100); // Allow time for event processing

            // Assert
            Assert.IsTrue(eventFired, "ButtonDown event should have fired");
        }

        [TestMethod]
        public void ButtonUp_WhenReleased_FiresEvent()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            bool eventFired = false;
            _buttonWithLed.ButtonUp += (sender, args) => eventFired = true;

            // Act
            SimulateButtonPressAndRelease();
            Thread.Sleep(100); // Allow time for event processing

            // Assert
            Assert.IsTrue(eventFired, "ButtonUp event should have fired");
        }

        [TestMethod]
        public void ButtonEvents_WhenMultipleSubscribers_AllReceiveEvents()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            int downEventCount = 0;
            int upEventCount = 0;
            
            _buttonWithLed.ButtonDown += (sender, args) => downEventCount++;
            _buttonWithLed.ButtonDown += (sender, args) => downEventCount++;
            _buttonWithLed.ButtonUp += (sender, args) => upEventCount++;
            _buttonWithLed.ButtonUp += (sender, args) => upEventCount++;

            // Act
            SimulateButtonPressAndRelease();
            Thread.Sleep(100); // Allow time for event processing

            // Assert
            Assert.AreEqual(2, downEventCount, "Both ButtonDown event handlers should have fired");
            Assert.AreEqual(2, upEventCount, "Both ButtonUp event handlers should have fired");
        }

        #endregion

        #region LED State Tests

        [TestMethod]
        public void LedState_SetToOn_ReflectsCorrectly()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act
            _buttonWithLed.LedState = PushButtonLedState.On;

            // Assert
            Assert.AreEqual(PushButtonLedState.On, _buttonWithLed.LedState);
            
            // Verify LED pin state for mock controller
            if (!UseRealHardware && _mockGpioController != null)
            {
                var ledValue = _mockGpioController.Read(TestLedPin);
                Assert.AreEqual(PinValue.High, ledValue, "LED pin should be HIGH when LedState is On");
            }
        }

        [TestMethod]
        public void LedState_SetToOff_ReflectsCorrectly()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedState = PushButtonLedState.On; // First turn it on

            // Act
            _buttonWithLed.LedState = PushButtonLedState.Off;

            // Assert
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);
            
            // Verify LED pin state for mock controller
            if (!UseRealHardware && _mockGpioController != null)
            {
                var ledValue = _mockGpioController.Read(TestLedPin);
                Assert.AreEqual(PinValue.Low, ledValue, "LED pin should be LOW when LedState is Off");
            }
        }

        [TestMethod]
        public void LedState_WithoutLedPin_RemainsNotUsed()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed(ledPin: null);

            // Act & Assert
            _buttonWithLed.LedState = PushButtonLedState.On; // Should be ignored
            Assert.AreEqual(PushButtonLedState.NotUsed, _buttonWithLed.LedState);
        }

        [TestMethod]
        public void LedState_MultipleChanges_ReflectsCorrectly()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act & Assert sequence
            _buttonWithLed.LedState = PushButtonLedState.On;
            Assert.AreEqual(PushButtonLedState.On, _buttonWithLed.LedState);

            _buttonWithLed.LedState = PushButtonLedState.Off;
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);

            _buttonWithLed.LedState = PushButtonLedState.On;
            Assert.AreEqual(PushButtonLedState.On, _buttonWithLed.LedState);
        }

        #endregion

        #region Debounce Tests

        [TestMethod]
        public void ButtonPress_WithDebouncing_IgnoresRapidChanges()
        {
            // Arrange
            // Ensure doublePress >= 3 * debounceTime per ButtonBase contract
            _buttonWithLed = CreateButtonWithLed(doublePress: TimeSpan.FromMilliseconds(1000), debounceTime: TimeSpan.FromMilliseconds(200));
            int eventCount = 0;
            _buttonWithLed.ButtonDown += (sender, args) => eventCount++;

            // Act - Simulate rapid presses within debounce time
            SimulateButtonPress();
            Thread.Sleep(50);
            SimulateButtonRelease();
            Thread.Sleep(50);
            SimulateButtonPress();
            Thread.Sleep(50);
            SimulateButtonRelease();
            Thread.Sleep(300); // Wait longer than debounce time

            // Assert
            Assert.AreEqual(1, eventCount, "Only one ButtonDown event should fire due to debouncing");
        }

        [TestMethod]
        public void ButtonPress_AfterDebounceTime_FiresNewEvent()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed(debounceTime: TimeSpan.FromMilliseconds(100));
            int eventCount = 0;
            _buttonWithLed.ButtonDown += (sender, args) => eventCount++;

            // Act - Two presses separated by debounce time
            SimulateButtonPressAndRelease(50, 50);
            Thread.Sleep(150); // Wait longer than debounce time
            SimulateButtonPressAndRelease(50, 50);
            Thread.Sleep(150); // Allow final event processing

            // Assert
            Assert.AreEqual(2, eventCount, "Two ButtonDown events should fire when separated by debounce time");
        }

        #endregion

        #region Disposal Tests

        [TestMethod]
        public void Dispose_AfterCreation_DisposesCleanly()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            bool eventFired = false;
            _buttonWithLed.ButtonDown += (sender, args) => eventFired = true;

            // Act
            _buttonWithLed.Dispose();

            // Assert - Should not throw
            // After disposal, events should not fire
            if (!UseRealHardware && _mockGpioController != null)
            {
                // After disposal, pins are closed; simulate may throw. Ignore such exceptions and only assert no event fired.
                try
                {
                    SimulateButtonPressAndRelease();
                }
                catch
                {
                    // Expected in disposed state where pins are closed.
                }
                Thread.Sleep(100);
                Assert.IsFalse(eventFired, "Events should not fire after disposal");
            }
        }

        [TestMethod]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act & Assert
            _buttonWithLed.Dispose();
            _buttonWithLed.Dispose(); // Should not throw
            _buttonWithLed.Dispose(); // Should not throw
        }

        #endregion

        #region Stress Tests

        [TestMethod]
        public void StressTest_RapidButtonPresses_HandlesCorrectly()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed(debounceTime: TimeSpan.FromMilliseconds(10));
            int eventCount = 0;
            _buttonWithLed.ButtonDown += (sender, args) => Interlocked.Increment(ref eventCount);

            // Act - Simulate rapid button presses
            for (int i = 0; i < 50; i++)
            {
                SimulateButtonPressAndRelease(5, 5);
            }
            Thread.Sleep(500); // Allow all events to process

            // Assert
            Assert.IsTrue(eventCount > 0, "At least some button events should have been processed");
            Assert.IsTrue(eventCount <= 50, "Event count should not exceed number of presses");
        }

        [TestMethod]
        public void StressTest_RapidLedStateChanges_HandlesCorrectly()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act & Assert - Rapid LED state changes should not throw
            for (int i = 0; i < 100; i++)
            {
                _buttonWithLed.LedState = i % 2 == 0 ? PushButtonLedState.On : PushButtonLedState.Off;
            }

            // Final state should be reflected correctly
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);
        }

        [TestMethod]
        public void StressTest_ConcurrentAccess_HandlesCorrectly()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            var tasks = new Task[10];

            // Act - Concurrent access to LED state and button simulation
            for (int i = 0; i < tasks.Length; i++)
            {
                int taskIndex = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        _buttonWithLed.LedState = taskIndex % 2 == 0 ? PushButtonLedState.On : PushButtonLedState.Off;
                        Thread.Sleep(10);
                    }
                });
            }

            // Wait for all tasks to complete
            Task.WaitAll(tasks, TimeSpan.FromSeconds(5));

            // Assert
            Assert.IsNotNull(_buttonWithLed.LedState); // Should have some valid state
        }

        #endregion
    }
}
