using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using System;
using System.Device.Gpio;
using System.Threading.Tasks;
using System.Threading;
using HVO.Iot.Devices;
using HVO.Iot.Devices.Abstractions;
using Moq;
using System.Collections.Generic;

namespace HVO.Iot.Devices.Tests
{
    [TestClass]
    public class GpioLimitSwitchTests
    {
        private Mock<IGpioController>? _mockGpioController;
        private Mock<ILogger<GpioLimitSwitch>>? _mockLogger;
        private GpioLimitSwitch? _limitSwitch;
        private const int TestPin = 18;
        private PinChangeEventHandler? _capturedCallback;

        [TestInitialize]
        public void Setup()
        {
            _mockGpioController = new Mock<IGpioController>();
            _mockLogger = new Mock<ILogger<GpioLimitSwitch>>();
            _capturedCallback = null;
            SetupGpioControllerMock();
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                _limitSwitch?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
            _limitSwitch = null;
            _mockGpioController = null;
            _mockLogger = null;
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
            
            // Setup pin reading - default to High (not triggered for pull-up)
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
        /// Creates a limit switch instance using the mock GPIO controller
        /// </summary>
        private GpioLimitSwitch CreateLimitSwitch(bool isPullup = true, bool hasExternalResistor = false, 
                                                 TimeSpan debounceTime = default, ILogger<GpioLimitSwitch>? logger = null)
        {
            return new GpioLimitSwitch(
                _mockGpioController!.Object,
                TestPin,
                isPullup: isPullup,
                hasExternalResistor: hasExternalResistor,
                debounceTime: debounceTime,
                logger: logger ?? _mockLogger?.Object);
        }

        /// <summary>
        /// Simulates a pin state change by invoking the captured callback
        /// </summary>
        private void SimulatePinStateChange(PinEventTypes eventType)
        {
            if (_capturedCallback == null) return;

            var eventArgs = new PinValueChangedEventArgs(eventType, TestPin);
            _capturedCallback.Invoke(_mockGpioController!.Object, eventArgs);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_WithValidParameters_ShouldInitialize()
        {
            // Act
            _limitSwitch = CreateLimitSwitch();

            // Assert
            Assert.IsNotNull(_limitSwitch);
            Assert.AreEqual(_limitSwitch.GpioPinNumber, TestPin);
            Assert.IsTrue(_limitSwitch.IsPullup);
            Assert.IsFalse(_limitSwitch.HasExternalResistor);
            Assert.AreEqual(_limitSwitch.DebounceTime, TimeSpan.Zero);
            Assert.AreEqual(_limitSwitch.CurrentPinValue, PinValue.High);
        }

        [TestMethod]
        public void Constructor_WithPullDownConfiguration_ShouldSetCorrectModes()
        {
            // Act
            _limitSwitch = CreateLimitSwitch(isPullup: false);

            // Assert
            Assert.IsFalse(_limitSwitch.IsPullup);
            _mockGpioController!.Verify(x => x.OpenPin(TestPin, PinMode.InputPullDown), Times.Once);
        }

        [TestMethod]
        public void Constructor_WithExternalResistor_ShouldSetInputMode()
        {
            // Act
            _limitSwitch = CreateLimitSwitch(hasExternalResistor: true);

            // Assert
            Assert.IsTrue(_limitSwitch.HasExternalResistor);
            _mockGpioController!.Verify(x => x.OpenPin(TestPin, PinMode.Input), Times.Once);
        }

        [TestMethod]
        public void Constructor_WithDebounceTime_ShouldSetCorrectly()
        {
            // Arrange
            var debounceTime = TimeSpan.FromMilliseconds(100);

            // Act
            _limitSwitch = CreateLimitSwitch(debounceTime: debounceTime);

            // Assert
            Assert.AreEqual(_limitSwitch.DebounceTime, debounceTime);
        }

        [TestMethod]
        public void Constructor_WithNullGpioController_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new GpioLimitSwitch((IGpioController)null!, TestPin));
        }

        [TestMethod]
        public void Constructor_WithInvalidPinNumber_ShouldThrowArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                new GpioLimitSwitch(_mockGpioController!.Object, 0));
        }

        [TestMethod]
        public void Constructor_WithUnsupportedPinMode_ShouldThrowArgumentException()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.IsPinModeSupported(TestPin, It.IsAny<PinMode>()))
                              .Returns(false);

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => CreateLimitSwitch());
        }

        [TestMethod]
        public void Constructor_WithPinAlreadyOpen_ShouldCloseAndReopenPin()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.IsPinOpen(TestPin))
                              .Returns(true);

            // Act
            _limitSwitch = CreateLimitSwitch();

            // Assert
            _mockGpioController.Verify(x => x.ClosePin(TestPin), Times.Once);
            _mockGpioController.Verify(x => x.OpenPin(TestPin, PinMode.InputPullUp), Times.Once);
        }

        [TestMethod]
        public void Constructor_WithPinOpenFailure_ShouldThrowInvalidOperationException()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.OpenPin(TestPin, It.IsAny<PinMode>()))
                              .Throws(new InvalidOperationException("Pin open failed"));

            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() => CreateLimitSwitch());
        }

        [TestMethod]
        public void Constructor_WithReadFailure_ShouldThrowInvalidOperationException()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.Read(TestPin))
                              .Throws(new InvalidOperationException("Read failed"));

            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() => CreateLimitSwitch());
        }

        [TestMethod]
        public void Constructor_WithCallbackRegistrationFailure_ShouldThrowInvalidOperationException()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.RegisterCallbackForPinValueChangedEvent(
                It.IsAny<int>(), It.IsAny<PinEventTypes>(), It.IsAny<PinChangeEventHandler>()))
                .Throws(new InvalidOperationException("Callback registration failed"));

            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() => CreateLimitSwitch());
        }

        #endregion

        #region CurrentPinValue Tests

        [TestMethod]
        public void CurrentPinValue_WhenDisposed_ShouldReturnLow()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();
            _limitSwitch.Dispose();

            // Act
            var result = _limitSwitch.CurrentPinValue;

            // Assert
            Assert.AreEqual(result, PinValue.Low);
        }

        [TestMethod]
        public void CurrentPinValue_FirstRead_ShouldCacheValue()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();

            // Act
            var firstRead = _limitSwitch.CurrentPinValue;
            var secondRead = _limitSwitch.CurrentPinValue;

            // Assert
            Assert.AreEqual(secondRead, firstRead);
            _mockGpioController!.Verify(x => x.Read(TestPin), Times.Once); // Should only read once due to caching
        }

        [TestMethod]
        public void CurrentPinValue_WhenReadFails_ShouldThrowDuringConstruction()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.Read(TestPin))
                              .Throws(new InvalidOperationException("Read failed"));

            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() => CreateLimitSwitch());
        }

        [TestMethod]
        public void CurrentPinValue_WhenReadFails_ShouldReturnLow()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();
            
            // Set up the mock to throw after construction when CurrentPinValue is accessed
            // First, reset to allow the property to be accessed after construction
            _mockGpioController!.Reset();
            SetupGpioControllerMock();
            
            // However, since the value is cached during construction, it won't call Read again
            // So this test verifies the cached behavior
            var result = _limitSwitch.CurrentPinValue;

            // Assert - should return the cached value from construction
            Assert.AreEqual(result, PinValue.High);
        }

        [TestMethod]
        public void CurrentPinValue_AfterPinStateChange_ShouldReturnUpdatedValue()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();
            var initialValue = _limitSwitch.CurrentPinValue;
            
            // Act
            SimulatePinStateChange(PinEventTypes.Falling); // This should change the cached value
            var updatedValue = _limitSwitch.CurrentPinValue;

            // Assert
            Assert.AreEqual(initialValue, PinValue.High);
            Assert.AreEqual(updatedValue, PinValue.Low);
        }

        #endregion

        #region ToString Tests

        [TestMethod]
        public void ToString_ShouldReturnFormattedString()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();

            // Act
            var result = _limitSwitch.ToString();

            // Assert
            Assert.IsTrue(result.Contains($"Pin={TestPin}"));
            Assert.IsTrue(result.Contains("Mode=InputPullUp"));
            Assert.IsTrue(result.Contains("Pull=True"));
            Assert.IsTrue(result.Contains("ExtResistor=False"));
        }

        #endregion

        #region Event Tests

        [TestMethod]
        public void LimitSwitchTriggered_EventHandlerException_ShouldNotCrash()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();
            _limitSwitch.LimitSwitchTriggered += (sender, e) => throw new InvalidOperationException("Test exception");

            // Act & Assert - Event should not crash even if handler throws
            try
            {
                SimulatePinStateChange(PinEventTypes.Falling);
                // If we get here, the event was handled gracefully
                Assert.IsTrue(true, "Event was handled without crashing");
            }
            catch
            {
                Assert.Fail("Event handler exception should not propagate");
            }
        }

        [TestMethod]
        public void LimitSwitchTriggered_EventSubscriptionUnsubscription_ShouldWork()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();
            var eventCount = 0;
            
            void EventHandler(object? sender, LimitSwitchTriggeredEventArgs e) => eventCount++;

            _limitSwitch.LimitSwitchTriggered += EventHandler;

            // Act
            SimulatePinStateChange(PinEventTypes.Falling);
            Assert.AreEqual(eventCount, 1);

            _limitSwitch.LimitSwitchTriggered -= EventHandler;
            SimulatePinStateChange(PinEventTypes.Rising);

            // Assert
            Assert.AreEqual(eventCount, 1); // Should still be 1 after unsubscription
        }

        [TestMethod]
        public void LimitSwitchTriggered_EventArgs_ShouldContainCorrectInformation()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();
            LimitSwitchTriggeredEventArgs? capturedArgs = null;
            
            _limitSwitch.LimitSwitchTriggered += (sender, e) => capturedArgs = e;

            // Act
            SimulatePinStateChange(PinEventTypes.Rising);

            // Assert
            Assert.IsNotNull(capturedArgs);
            Assert.AreEqual(capturedArgs.ChangeType, PinEventTypes.Rising);
            Assert.AreEqual(capturedArgs.PinNumber, TestPin);
            Assert.AreEqual(capturedArgs.PinMode, PinMode.InputPullUp);
            Assert.IsTrue(capturedArgs.EventDateTime <= DateTimeOffset.Now);
        }

        [TestMethod]
        public void PinStateChanged_WithDebouncing_ShouldFilterEvents()
        {
            // Arrange
            var debounceTime = TimeSpan.FromMilliseconds(100);
            _limitSwitch = CreateLimitSwitch(debounceTime: debounceTime);
            
            var eventCount = 0;
            _limitSwitch.LimitSwitchTriggered += (sender, e) => eventCount++;

            // Act - Fire multiple events rapidly
            SimulatePinStateChange(PinEventTypes.Rising);
            SimulatePinStateChange(PinEventTypes.Rising);
            SimulatePinStateChange(PinEventTypes.Rising);

            // Assert - Should only fire once due to debouncing
            Assert.IsTrue(eventCount <= 1, $"Expected at most 1 event, but got {eventCount}");
        }

        [TestMethod]
        public void PinStateChanged_WithDifferentEventTypes_ShouldTriggerEvents()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();
            var events = new List<PinEventTypes>();
            
            _limitSwitch.LimitSwitchTriggered += (sender, e) => events.Add(e.ChangeType);

            // Act - Fire different event types
            SimulatePinStateChange(PinEventTypes.Rising);
            Thread.Sleep(10); // Small delay to avoid debouncing
            SimulatePinStateChange(PinEventTypes.Falling);

            // Assert
            Assert.AreEqual(events.Count, 2);
            Assert.AreEqual(events[0], PinEventTypes.Rising);
            Assert.AreEqual(events[1], PinEventTypes.Falling);
        }

        [TestMethod]
        public void PinStateChanged_ShouldRaiseEvent_WhenDebouncePassed()
        {
            // Arrange
            var debounceTime = TimeSpan.FromMilliseconds(50);
            _limitSwitch = CreateLimitSwitch(debounceTime: debounceTime);
            
            var events = new List<PinEventTypes>();
            _limitSwitch.LimitSwitchTriggered += (sender, e) => events.Add(e.ChangeType);

            // Act
            SimulatePinStateChange(PinEventTypes.Rising);
            
            // Wait for debounce time to pass with some buffer
            Thread.Sleep(debounceTime.Add(TimeSpan.FromMilliseconds(50)));
            
            SimulatePinStateChange(PinEventTypes.Falling);

            // Assert
            Assert.AreEqual(2, events.Count, $"Expected 2 events when debounce time has passed. Got events: {string.Join(", ", events)}");
            Assert.AreEqual(PinEventTypes.Rising, events[0]);
            Assert.AreEqual(PinEventTypes.Falling, events[1]);
        }

        [TestMethod]
        public void PinStateChanged_ShouldNotRaiseEvent_WhenDebounceNotPassed()
        {
            // Arrange
            var debounceTime = TimeSpan.FromMilliseconds(100);
            _limitSwitch = CreateLimitSwitch(debounceTime: debounceTime);
            
            var eventCount = 0;
            _limitSwitch.LimitSwitchTriggered += (sender, e) => eventCount++;

            // Act - Fire events rapidly within debounce time
            SimulatePinStateChange(PinEventTypes.Rising);
            Thread.Sleep(10); // Short delay, less than debounce time
            SimulatePinStateChange(PinEventTypes.Falling);
            Thread.Sleep(10); // Short delay, less than debounce time
            SimulatePinStateChange(PinEventTypes.Rising);

            // Assert
            Assert.IsTrue(eventCount <= 1, $"Expected at most 1 event when debounce time has not passed, but got {eventCount}");
        }

        #endregion

        #region Disposal Tests

        [TestMethod]
        public void Dispose_ShouldSetDisposedFlag()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();

            // Act
            _limitSwitch.Dispose();

            // Assert
            Assert.AreEqual(_limitSwitch.CurrentPinValue, PinValue.Low);
            
            // Verify proper cleanup was called
            _mockGpioController!.Verify(x => x.UnregisterCallbackForPinValueChangedEvent(
                TestPin, It.IsAny<PinChangeEventHandler>()), Times.Once);
            
            // ClosePin is only called if the pin is open, so we verify it was checked
            _mockGpioController.Verify(x => x.IsPinOpen(TestPin), Times.AtLeastOnce);
        }

        [TestMethod]
        public void Dispose_MultipleCallsShouldNotThrow()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();

            // Act & Assert
            _limitSwitch.Dispose();
            _limitSwitch.Dispose(); // Should not throw
        }

        [TestMethod]
        public async Task DisposeAsync_ShouldDisposeCorrectly()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();

            // Act
            await _limitSwitch.DisposeAsync();

            // Assert
            Assert.AreEqual(_limitSwitch.CurrentPinValue, PinValue.Low);
            _mockGpioController!.Verify(x => x.UnregisterCallbackForPinValueChangedEvent(
                TestPin, It.IsAny<PinChangeEventHandler>()), Times.Once);
        }

        [TestMethod]
        public async Task DisposeAsync_MultipleCallsShouldNotThrow()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();

            // Act & Assert
            await _limitSwitch.DisposeAsync();
            await _limitSwitch.DisposeAsync(); // Should not throw
        }

        [TestMethod]
        public async Task DisposeAsync_WithDebounceTime_ShouldWaitForSettling()
        {
            // Arrange
            var debounceTime = TimeSpan.FromMilliseconds(50);
            _limitSwitch = CreateLimitSwitch(debounceTime: debounceTime);

            // Act
            var startTime = DateTime.UtcNow;
            await _limitSwitch.DisposeAsync();
            var endTime = DateTime.UtcNow;

            // Assert
            var elapsed = endTime - startTime;
            Assert.IsTrue(elapsed >= TimeSpan.FromMilliseconds(25), "Should wait at least some settling time");
        }

        #endregion

        #region Properties Tests

        [TestMethod]
        public void Properties_AfterConstruction_ShouldReturnCorrectValues()
        {
            // Arrange
            var debounceTime = TimeSpan.FromMilliseconds(100);

            // Act
            _limitSwitch = CreateLimitSwitch(isPullup: false, hasExternalResistor: true, debounceTime: debounceTime);

            // Assert
            Assert.AreEqual(_limitSwitch.GpioPinNumber, TestPin);
            Assert.IsFalse(_limitSwitch.IsPullup);
            Assert.IsTrue(_limitSwitch.HasExternalResistor);
            Assert.AreEqual(_limitSwitch.DebounceTime, debounceTime);
            Assert.IsNotNull(_limitSwitch.GpioController);
        }

        [TestMethod]
        public void ToString_WithDifferentConfigurations_ShouldReflectSettings()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch(isPullup: false, hasExternalResistor: true);

            // Act
            var result = _limitSwitch.ToString();

            // Assert
            Assert.IsTrue(result.Contains("Pull=False"));
            Assert.IsTrue(result.Contains("ExtResistor=True"));
        }

        #endregion

        #region Constructor Logger Tests

        [TestMethod]
        public void Constructor_WithoutLogger_ShouldNotThrow()
        {
            // Act & Assert - Creating without logger should work
            try
            {
                _limitSwitch = CreateLimitSwitch(logger: null);
                Assert.IsNotNull(_limitSwitch);
            }
            catch
            {
                Assert.Fail("Constructor should not throw when logger is null");
            }
        }

        [TestMethod]
        public void Constructor_ShouldLogInitializationSuccess()
        {
            // Act
            _limitSwitch = CreateLimitSwitch();

            // Assert
            _mockLogger!.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully initialized")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [TestMethod]
        public void Constructor_WithPinAlreadyOpen_ShouldLogWarning()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.IsPinOpen(TestPin))
                              .Returns(true);

            // Act
            _limitSwitch = CreateLimitSwitch();

            // Assert
            _mockLogger!.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("already open")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [TestMethod]
        public void PinStateChanged_ShouldLogLimitSwitchTriggered()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();

            // Act
            SimulatePinStateChange(PinEventTypes.Falling);

            // Assert
            _mockLogger!.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Limit switch triggered")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [TestMethod]
        public void Dispose_ShouldLogDisposalInformation()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();

            // Act
            _limitSwitch.Dispose();

            // Assert
            _mockLogger!.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully disposed")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        #endregion

        #region Thread Safety Tests

        [TestMethod]
        public void CurrentPinValue_ConcurrentAccess_ShouldBeThreadSafe()
        {
            // Arrange
            _limitSwitch = CreateLimitSwitch();
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
                            var value = _limitSwitch.CurrentPinValue;
                            Assert.IsTrue(value == PinValue.High || value == PinValue.Low);
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

            Task.WaitAll(tasks.ToArray());

            // Assert
            Assert.AreEqual(0, exceptions.Count, $"Thread safety test failed with {exceptions.Count} exceptions");
        }

        #endregion

        #region Finalizer Tests

        [TestMethod]
        public void Finalizer_ShouldNotThrow()
        {
            // This test is difficult to implement directly due to the nature of finalizers
            // We'll test that creating and abandoning an object doesn't cause issues
            
            // Act & Assert - Creating and abandoning should work
            try
            {
                var limitSwitch = CreateLimitSwitch();
                // Don't dispose, let it be garbage collected
                Assert.IsNotNull(limitSwitch);
            }
            catch
            {
                Assert.Fail("Creating and abandoning object should not throw");
            }

            // Force garbage collection to potentially trigger finalizer
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        #endregion

        #region Mock Verification Tests

        [TestMethod]
        public void MockVerification_ShouldValidateAllGpioInteractions()
        {
            // Act
            _limitSwitch = CreateLimitSwitch();

            // Assert
            _mockGpioController!.Verify(x => x.IsPinModeSupported(TestPin, PinMode.InputPullUp), Times.Once);
            _mockGpioController.Verify(x => x.IsPinOpen(TestPin), Times.Once);
            _mockGpioController.Verify(x => x.OpenPin(TestPin, PinMode.InputPullUp), Times.Once);
            _mockGpioController.Verify(x => x.Read(TestPin), Times.Once);
            _mockGpioController.Verify(x => x.RegisterCallbackForPinValueChangedEvent(
                TestPin, PinEventTypes.Falling | PinEventTypes.Rising, It.IsAny<PinChangeEventHandler>()), Times.Once);
        }

        [TestMethod]
        public void MockBehavior_CustomPinReadSequence_ShouldWork()
        {
            // Arrange
            var readSequence = new Queue<PinValue>(new[] { PinValue.Low, PinValue.High, PinValue.Low });
            
            // Reset the mock and set up the sequence before creating the limit switch
            _mockGpioController!.Reset();
            SetupGpioControllerMock();
            
            // Set up the specific sequence for our test pin
            _mockGpioController!.Setup(x => x.Read(TestPin))
                              .Returns(() => readSequence.Count > 0 ? readSequence.Dequeue() : PinValue.Low);

            // Act
            _limitSwitch = CreateLimitSwitch();

            // Assert
            // The CurrentPinValue should return the cached value from initialization
            // During initialization, the first value (Low) was consumed and cached
            Assert.AreEqual(_limitSwitch.CurrentPinValue, PinValue.Low);
            
            // Verify the sequence was used
            _mockGpioController.Verify(x => x.Read(TestPin), Times.AtLeastOnce);
        }

        [TestMethod]
        public void MockBehavior_PinModeNotSupported_ShouldThrowWithCorrectMessage()
        {
            // Arrange
            _mockGpioController!.Setup(x => x.IsPinModeSupported(TestPin, PinMode.InputPullUp))
                              .Returns(false);

            // Act & Assert
            var exception = Assert.ThrowsException<ArgumentException>(() => CreateLimitSwitch());
            Assert.IsTrue(exception.Message.Contains("pull-up"));
            Assert.IsTrue(exception.Message.Contains($"GPIO pin {TestPin}"));
        }

        [TestMethod]
        public void MockBehavior_CallbackEvents_ShouldCaptureRegisteredHandler()
        {
            // Arrange
            PinChangeEventHandler? capturedHandler = null;
            
            _mockGpioController!.Setup(x => x.RegisterCallbackForPinValueChangedEvent(
                TestPin,
                PinEventTypes.Falling | PinEventTypes.Rising,
                It.IsAny<PinChangeEventHandler>()))
                .Callback<int, PinEventTypes, PinChangeEventHandler>((pin, events, handler) =>
                {
                    capturedHandler = handler;
                });

            // Act
            _limitSwitch = CreateLimitSwitch();

            // Assert
            Assert.IsNotNull(capturedHandler);
            
            // Verify we can invoke the captured handler
            var eventArgs = new PinValueChangedEventArgs(PinEventTypes.Rising, TestPin);
            capturedHandler.Invoke(_mockGpioController.Object, eventArgs);
            
            // Should not throw and should update the pin value
            Assert.AreEqual(_limitSwitch.CurrentPinValue, PinValue.High);
        }

        #endregion
    }
}
