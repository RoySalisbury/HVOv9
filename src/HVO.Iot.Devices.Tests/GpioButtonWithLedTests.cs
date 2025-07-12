using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Device.Gpio;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HVO.Iot.Devices;
using HVO.Iot.Devices.Abstractions;

namespace HVO.Iot.Devices.Tests
{
    [TestClass]
    public class GpioButtonWithLedTests
    {
        private Mock<IGpioController>? _mockGpioController;
        private GpioButtonWithLed? _buttonWithLed;
        private PinChangeEventHandler? _capturedCallback;
        private const int ButtonPin = 18;
        private const int LedPin = 24;
        private const int TestPin = ButtonPin; // For readability in tests
        private const int TestLedPin = LedPin; // For readability in tests

        [TestInitialize]
        public void Setup()
        {
            _mockGpioController = new Mock<IGpioController>();
            _capturedCallback = null;
            SetupGpioControllerMock();
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
            _mockGpioController = null;
        }

        /// <summary>
        /// Sets up the basic GPIO controller mock behavior that all tests need
        /// </summary>
        private void SetupGpioControllerMock()
        {
            if (_mockGpioController == null) return;

            // Setup basic pin mode support
            _mockGpioController.Setup(x => x.IsPinModeSupported(It.IsAny<int>(), It.IsAny<PinMode>()))
                              .Returns(true);

            // Setup pin state - pins start closed
            _mockGpioController.Setup(x => x.IsPinOpen(It.IsAny<int>()))
                              .Returns(false);

            // Setup pin operations
            _mockGpioController.Setup(x => x.OpenPin(It.IsAny<int>(), It.IsAny<PinMode>()));
            _mockGpioController.Setup(x => x.ClosePin(It.IsAny<int>()));
            _mockGpioController.Setup(x => x.Write(It.IsAny<int>(), It.IsAny<PinValue>()));
            
            // Setup button pin reading - default to not pressed (pull-up = High)
            _mockGpioController.Setup(x => x.Read(TestPin))
                              .Returns(PinValue.High);

            // Setup callback registration and capture the callback
            _mockGpioController.Setup(x => x.RegisterCallbackForPinValueChangedEvent(
                It.IsAny<int>(),
                It.IsAny<PinEventTypes>(),
                It.IsAny<PinChangeEventHandler>()))
                .Callback<int, PinEventTypes, PinChangeEventHandler>((pin, events, callback) =>
                {
                    if (pin == TestPin)
                    {
                        _capturedCallback = callback;
                    }
                });
                
            _mockGpioController.Setup(x => x.UnregisterCallbackForPinValueChangedEvent(
                It.IsAny<int>(),
                It.IsAny<PinChangeEventHandler>()));
        }

        /// <summary>
        /// Creates a button with LED instance using the mock GPIO controller
        /// </summary>
        private GpioButtonWithLed CreateButtonWithLed(bool isPullUp = true, bool hasExternalResistor = false, TimeSpan debounceTime = default)
        {
            return new GpioButtonWithLed(
                ButtonPin,
                LedPin,
                TimeSpan.FromTicks(15000000), // DefaultDoublePressTicks
                TimeSpan.FromMilliseconds(2000), // DefaultHoldingMilliseconds
                isPullUp: isPullUp,
                hasExternalResistor: hasExternalResistor,
                gpioController: _mockGpioController!.Object,
                debounceTime: debounceTime);
        }

        /// <summary>
        /// Simulates a button press or release by triggering the GPIO callback
        /// </summary>
        private void SimulateButtonStateChange(PinEventTypes eventType, bool isPressed = true)
        {
            if (_buttonWithLed == null || _mockGpioController == null || _capturedCallback == null) return;

            // Create event args
            var eventArgs = new PinValueChangedEventArgs(eventType, TestPin);

            // Call the captured callback
            _capturedCallback.Invoke(_mockGpioController.Object, eventArgs);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_ValidParameters_ShouldInitializeCorrectly()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed();

            // Assert
            Assert.IsNotNull(_buttonWithLed);
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);
            Assert.AreEqual(PushButtonLedOptions.FollowPressedState, _buttonWithLed.LedOptions);
            Assert.IsFalse(_buttonWithLed.HasExternalResistor);
            Assert.AreEqual(TimeSpan.Zero, _buttonWithLed.DebounceTime);
        }

        [TestMethod]
        public void Constructor_WithExternalResistor_ShouldSetProperty()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed(hasExternalResistor: true);

            // Assert
            Assert.IsTrue(_buttonWithLed.HasExternalResistor);
        }

        [TestMethod]
        public void Constructor_WithDebounceTime_ShouldSetProperty()
        {
            // Arrange
            var debounceTime = TimeSpan.FromMilliseconds(50);

            // Act
            _buttonWithLed = CreateButtonWithLed(debounceTime: debounceTime);

            // Assert
            Assert.AreEqual(debounceTime, _buttonWithLed.DebounceTime);
        }

        [TestMethod]
        public void Constructor_SamePinNumbers_ShouldThrowArgumentException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() =>
                new GpioButtonWithLed(TestPin, TestPin, TimeSpan.FromTicks(15000000), TimeSpan.FromMilliseconds(2000), gpioController: _mockGpioController!.Object));
        }

        [TestMethod]
        public void Constructor_UnsupportedButtonPinMode_ShouldThrowArgumentException()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.IsPinModeSupported(TestPin, It.IsAny<PinMode>()))
                              .Returns(false);

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => CreateButtonWithLed());
        }

        [TestMethod]
        public void Constructor_UnsupportedLedPinMode_ShouldThrowArgumentException()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.IsPinModeSupported(TestLedPin, PinMode.Output))
                              .Returns(false);

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => CreateButtonWithLed());
        }

        [TestMethod]
        public void Constructor_ShouldConfigurePinsCorrectly()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed();

            // Assert
            _mockGpioController!.Verify(x => x.OpenPin(TestPin, PinMode.InputPullUp), Times.Once);
            _mockGpioController.Verify(x => x.OpenPin(TestLedPin, PinMode.Output), Times.Once);
            _mockGpioController.Verify(x => x.RegisterCallbackForPinValueChangedEvent(
                TestPin,
                PinEventTypes.Falling | PinEventTypes.Rising,
                It.IsAny<PinChangeEventHandler>()), Times.Once);
        }

        [TestMethod]
        public void Constructor_WithExternalResistor_ShouldUseInputMode()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed(hasExternalResistor: true);

            // Assert
            _mockGpioController!.Verify(x => x.OpenPin(TestPin, PinMode.Input), Times.Once);
        }

        [TestMethod]
        public void Constructor_PullDownMode_ShouldConfigureCorrectly()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed(isPullUp: false);

            // Assert
            _mockGpioController!.Verify(x => x.OpenPin(TestPin, PinMode.InputPullDown), Times.Once);
        }

        #endregion

        #region LED State Tests

        [TestMethod]
        public void LedState_InitialValue_ShouldBeOff()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed();

            // Assert
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);
        }

        [TestMethod]
        public void LedState_SetToOn_ShouldUpdateValue()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act
            _buttonWithLed.LedState = PushButtonLedState.On;

            // Assert
            Assert.AreEqual(PushButtonLedState.On, _buttonWithLed.LedState);
            _mockGpioController!.Verify(x => x.Write(TestLedPin, PinValue.High), Times.AtLeastOnce);
        }

        [TestMethod]
        public void LedState_SetToOff_ShouldUpdateValue()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedState = PushButtonLedState.On;

            // Act
            _buttonWithLed.LedState = PushButtonLedState.Off;

            // Assert
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);
            _mockGpioController!.Verify(x => x.Write(TestLedPin, PinValue.Low), Times.AtLeastOnce);
        }

        [TestMethod]
        public void LedState_SetSameValue_ShouldNotTriggerGpioWrite()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _mockGpioController!.Invocations.Clear(); // Clear initialization calls

            // Act
            _buttonWithLed.LedState = PushButtonLedState.Off; // Same as initial value

            // Assert
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);
            _mockGpioController.Verify(x => x.Write(It.IsAny<int>(), It.IsAny<PinValue>()), Times.Never);
        }

        [TestMethod]
        public void LedState_AfterDisposal_ShouldReturnOff()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedState = PushButtonLedState.On;

            // Act
            _buttonWithLed.Dispose();
            var stateAfterDisposal = _buttonWithLed.LedState;

            // Assert
            Assert.AreEqual(PushButtonLedState.Off, stateAfterDisposal);
        }

        [TestMethod]
        public void LedState_SetAfterDisposal_ShouldNotThrow()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.Dispose();

            // Act & Assert - Should not throw, just ignore
            _buttonWithLed.LedState = PushButtonLedState.On;
            Assert.AreEqual(PushButtonLedState.Off, _buttonWithLed.LedState);
        }

        #endregion

        #region LED Options Tests

        [TestMethod]
        public void LedOptions_InitialValue_ShouldBeFollowPressedState()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed();

            // Assert
            Assert.AreEqual(PushButtonLedOptions.FollowPressedState, _buttonWithLed.LedOptions);
        }

        [TestMethod]
        public void LedOptions_SetToAlwaysOn_ShouldUpdateLedImmediately()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _mockGpioController!.Invocations.Clear(); // Clear initialization calls

            // Act
            _buttonWithLed.LedOptions = PushButtonLedOptions.AlwaysOn;

            // Assert
            Assert.AreEqual(PushButtonLedOptions.AlwaysOn, _buttonWithLed.LedOptions);
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.High), Times.AtLeastOnce);
        }

        [TestMethod]
        public void LedOptions_SetToAlwaysOff_ShouldUpdateLedImmediately()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedState = PushButtonLedState.On; // Turn on first
            _mockGpioController!.Invocations.Clear(); // Clear previous calls

            // Act
            _buttonWithLed.LedOptions = PushButtonLedOptions.AlwaysOff;

            // Assert
            Assert.AreEqual(PushButtonLedOptions.AlwaysOff, _buttonWithLed.LedOptions);
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.Low), Times.AtLeastOnce);
        }

        [TestMethod]
        public void LedOptions_SetSameValue_ShouldNotTriggerUpdate()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _mockGpioController!.Invocations.Clear(); // Clear initialization calls

            // Act
            _buttonWithLed.LedOptions = PushButtonLedOptions.FollowPressedState; // Same as initial

            // Assert
            Assert.AreEqual(PushButtonLedOptions.FollowPressedState, _buttonWithLed.LedOptions);
            _mockGpioController.Verify(x => x.Write(It.IsAny<int>(), It.IsAny<PinValue>()), Times.Never);
        }

        [TestMethod]
        public void LedOptions_SetAfterDisposal_ShouldNotThrow()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.Dispose();

            // Act & Assert - Should not throw, just ignore
            _buttonWithLed.LedOptions = PushButtonLedOptions.AlwaysOn;
            Assert.AreEqual(PushButtonLedOptions.FollowPressedState, _buttonWithLed.LedOptions); // Should remain unchanged
        }

        #endregion

        #region Button Press Simulation Tests

        [TestMethod]
        public void ButtonPress_WithFollowPressedState_ShouldTurnLedOn()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedOptions = PushButtonLedOptions.FollowPressedState;
            _mockGpioController!.Invocations.Clear();

            // Act
            SimulateButtonStateChange(PinEventTypes.Falling); // Pull-up: falling = pressed

            // Assert
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.High), Times.AtLeastOnce);
        }

        [TestMethod]
        public void ButtonRelease_WithFollowPressedState_ShouldTurnLedOff()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedOptions = PushButtonLedOptions.FollowPressedState;
            SimulateButtonStateChange(PinEventTypes.Falling); // Press first
            _mockGpioController!.Invocations.Clear();

            // Act
            SimulateButtonStateChange(PinEventTypes.Rising); // Pull-up: rising = released

            // Assert
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.Low), Times.AtLeastOnce);
        }

        [TestMethod]
        public void ButtonPress_WithAlwaysOn_ShouldKeepLedOn()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedOptions = PushButtonLedOptions.AlwaysOn;
            _mockGpioController!.Invocations.Clear();

            // Act
            SimulateButtonStateChange(PinEventTypes.Falling); // Press
            SimulateButtonStateChange(PinEventTypes.Rising);  // Release

            // Assert - LED should remain on regardless of button state
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.High), Times.AtLeastOnce);
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.Low), Times.Never);
        }

        [TestMethod]
        public void ButtonPress_WithAlwaysOff_ShouldKeepLedOff()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedOptions = PushButtonLedOptions.AlwaysOff;
            _mockGpioController!.Invocations.Clear();

            // Act
            SimulateButtonStateChange(PinEventTypes.Falling); // Press
            SimulateButtonStateChange(PinEventTypes.Rising);  // Release

            // Assert - LED should remain off regardless of button state
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.Low), Times.AtLeastOnce);
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.High), Times.Never);
        }

        [TestMethod]
        public void ButtonPress_PullDownMode_ShouldWorkCorrectly()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed(isPullUp: false);
            _buttonWithLed.LedOptions = PushButtonLedOptions.FollowPressedState;
            _mockGpioController!.Invocations.Clear();

            // Act
            SimulateButtonStateChange(PinEventTypes.Rising); // Pull-down: rising = pressed

            // Assert
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.High), Times.AtLeastOnce);
        }

        #endregion

        #region Disposal Tests

        [TestMethod]
        public void Dispose_ShouldTurnOffLedAndCleanupResources()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedState = PushButtonLedState.On;

            // Act
            _buttonWithLed.Dispose();

            // Assert
            _mockGpioController!.Verify(x => x.Write(TestLedPin, PinValue.Low), Times.AtLeastOnce);
            _mockGpioController.Verify(x => x.UnregisterCallbackForPinValueChangedEvent(
                TestPin, It.IsAny<PinChangeEventHandler>()), Times.Once);
        }

        [TestMethod]
        public async Task DisposeAsync_ShouldTurnOffLedAndCleanupResources()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            _buttonWithLed.LedState = PushButtonLedState.On;

            // Act
            await _buttonWithLed.DisposeAsync();

            // Assert
            _mockGpioController!.Verify(x => x.Write(TestLedPin, PinValue.Low), Times.AtLeastOnce);
            _mockGpioController.Verify(x => x.UnregisterCallbackForPinValueChangedEvent(
                TestPin, It.IsAny<PinChangeEventHandler>()), Times.Once);
        }

        [TestMethod]
        public void Dispose_MultipleCalls_ShouldNotThrow()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act & Assert - Should not throw
            _buttonWithLed.Dispose();
            _buttonWithLed.Dispose();
            _buttonWithLed.Dispose();
        }

        [TestMethod]
        public async Task DisposeAsync_MultipleCalls_ShouldNotThrow()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();

            // Act & Assert - Should not throw
            await _buttonWithLed.DisposeAsync();
            await _buttonWithLed.DisposeAsync();
            await _buttonWithLed.DisposeAsync();
        }

        #endregion

        #region Thread Safety Tests

        [TestMethod]
        public async Task LedState_ConcurrentAccess_ShouldBeThreadSafe()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            var tasks = new List<Task>();
            var exceptions = new List<Exception>();

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (int j = 0; j < 100; j++)
                        {
                            _buttonWithLed.LedState = j % 2 == 0 ? PushButtonLedState.On : PushButtonLedState.Off;
                            var currentState = _buttonWithLed.LedState;
                            // Verify we can read without exception
                            Assert.IsTrue(currentState == PushButtonLedState.On || currentState == PushButtonLedState.Off);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.AreEqual(0, exceptions.Count, $"Thread safety test failed with {exceptions.Count} exceptions");
            var finalState = _buttonWithLed.LedState;
            Assert.IsTrue(finalState == PushButtonLedState.On || finalState == PushButtonLedState.Off);
        }

        [TestMethod]
        public async Task LedOptions_ConcurrentAccess_ShouldBeThreadSafe()
        {
            // Arrange
            _buttonWithLed = CreateButtonWithLed();
            var tasks = new List<Task>();
            var exceptions = new List<Exception>();
            var options = new[] { PushButtonLedOptions.AlwaysOff, PushButtonLedOptions.AlwaysOn, PushButtonLedOptions.FollowPressedState };

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (int j = 0; j < 100; j++)
                        {
                            _buttonWithLed.LedOptions = options[j % options.Length];
                            var currentOptions = _buttonWithLed.LedOptions;
                            // Verify we can read without exception
                            Assert.IsTrue(options.Contains(currentOptions));
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.AreEqual(0, exceptions.Count, $"Thread safety test failed with {exceptions.Count} exceptions");
            var finalOptions = _buttonWithLed.LedOptions;
            Assert.IsTrue(options.Contains(finalOptions));
        }

        #endregion

        #region Enum Tests

        [TestMethod]
        public void PushButtonLedState_EnumValues_ShouldBeCorrect()
        {
            // Arrange & Act
            var offValue = PushButtonLedState.Off;
            var onValue = PushButtonLedState.On;

            // Assert
            Assert.AreEqual(0, (int)offValue);
            Assert.AreEqual(1, (int)onValue);
        }

        [TestMethod]
        public void PushButtonLedOptions_EnumValues_ShouldBeCorrect()
        {
            // Arrange & Act
            var alwaysOff = PushButtonLedOptions.AlwaysOff;
            var alwaysOn = PushButtonLedOptions.AlwaysOn;
            var followPressed = PushButtonLedOptions.FollowPressedState;

            // Assert
            Assert.AreEqual(0, (int)alwaysOff);
            Assert.AreEqual(1, (int)alwaysOn);
            Assert.AreEqual(2, (int)followPressed);
        }

        #endregion

        #region Mock Verification Tests

        [TestMethod]
        public void MockBehavior_PinModeSupport_ShouldBeVerified()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed();

            // Assert
            _mockGpioController!.Verify(x => x.IsPinModeSupported(TestPin, PinMode.InputPullUp), Times.Once);
            _mockGpioController.Verify(x => x.IsPinModeSupported(TestLedPin, PinMode.Output), Times.Once);
        }

        [TestMethod]
        public void MockBehavior_PinInitialization_ShouldBeVerified()
        {
            // Act
            _buttonWithLed = CreateButtonWithLed();

            // Assert
            _mockGpioController!.Verify(x => x.Read(TestPin), Times.Once); // Initial state read
            _mockGpioController.Verify(x => x.Write(TestLedPin, PinValue.Low), Times.Once); // Initial LED off
        }

        [TestMethod]
        public void MockBehavior_CustomPinReadSequence_ShouldWork()
        {
            // Arrange
            var readSequence = new Queue<PinValue>(new[] { PinValue.Low, PinValue.High, PinValue.Low });
            
            // Reset the mock and set up the sequence
            _mockGpioController!.Reset();
            SetupGpioControllerMock();
            
            // Set up the specific sequence for our test pin
            _mockGpioController!.Setup(x => x.Read(TestPin))
                              .Returns(() => readSequence.Count > 0 ? readSequence.Dequeue() : PinValue.Low);

            // Act
            _buttonWithLed = CreateButtonWithLed();

            // Assert
            _mockGpioController.Verify(x => x.Read(TestPin), Times.AtLeastOnce);
        }

        #endregion
    }
}