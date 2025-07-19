using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using System.Device.Gpio;
using HVO.Iot.Devices;

namespace HVO.WebSite.RoofControllerV4.Tests.Logic;

/// <summary>
/// Comprehensive tests for RoofControllerService covering all operations, logic paths, and scenarios.
/// </summary>
[TestClass]
public class RoofControllerServiceTests
{
    private readonly Mock<ILogger<RoofControllerService>> _mockLogger;
    private readonly RoofControllerOptions _options;
    private readonly MockGpioController _mockGpioController;
    private readonly RoofControllerService _roofController;

    public RoofControllerServiceTests()
    {
        _mockLogger = new Mock<ILogger<RoofControllerService>>();
        _options = new RoofControllerOptions
        {
            SafetyWatchdogTimeout = TimeSpan.FromSeconds(2), // Short timeout for testing
            RoofOpenedLimitSwitchPin = 17,
            RoofClosedLimitSwitchPin = 21,
            OpenRoofRelayPin = 24,
            CloseRoofRelayPin = 23,
            StopRoofRelayPin = 25,
            KeypadEnableRelayPin = 26,
            OpenRoofButtonPin = 8,
            CloseRoofButtonPin = 7,
            StopRoofButtonPin = 9,
            LimitSwitchDebounce = TimeSpan.FromMilliseconds(50),
            ButtonDebounce = TimeSpan.FromMilliseconds(50)
        };
        
        _mockGpioController = new MockGpioController();
        var optionsWrapper = Options.Create(_options);
        
        _roofController = new RoofControllerService(_mockLogger.Object, optionsWrapper, _mockGpioController);
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await _roofController.DisposeAsync();
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var logger = new Mock<ILogger<RoofControllerService>>();
        var options = Options.Create(new RoofControllerOptions());
        var gpioController = new MockGpioController();

        // Act
        var controller = new RoofControllerService(logger.Object, options, gpioController);

        // Assert
        controller.Should().NotBeNull();
        controller.IsInitialized.Should().BeFalse();
        controller.Status.Should().Be(RoofControllerStatus.NotInitialized);

        // Cleanup
        controller.Dispose();
        gpioController.Dispose();
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var options = Options.Create(new RoofControllerOptions());
        var gpioController = new MockGpioController();

        // Act & Assert
        var action = () => new RoofControllerService(null!, options, gpioController);
        action.Should().Throw<ArgumentNullException>();

        // Cleanup
        gpioController.Dispose();
    }

    [TestMethod]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var logger = new Mock<ILogger<RoofControllerService>>();
        var gpioController = new MockGpioController();

        // Act & Assert
        var action = () => new RoofControllerService(logger.Object, null!, gpioController);
        action.Should().Throw<ArgumentNullException>();

        // Cleanup
        gpioController.Dispose();
    }

    [TestMethod]
    public void Constructor_WithNullGpioController_ThrowsArgumentNullException()
    {
        // Arrange
        var logger = new Mock<ILogger<RoofControllerService>>();
        var options = Options.Create(new RoofControllerOptions());

        // Act & Assert
        var action = () => new RoofControllerService(logger.Object, options, null!);
        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Initialization Tests

    [TestMethod]
    public async Task Initialize_WhenNotInitialized_ReturnsSuccessAndSetsInitialized()
    {
        // Act
        var result = await _roofController.Initialize(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().BeTrue();
        _roofController.IsInitialized.Should().BeTrue();
    }

    [TestMethod]
    public async Task Initialize_WhenAlreadyInitialized_ReturnsFailure()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        var result = await _roofController.Initialize(CancellationToken.None);

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Initialize_WhenDisposed_ReturnsFailure()
    {
        // Arrange
        await _roofController.DisposeAsync();

        // Act
        var result = await _roofController.Initialize(CancellationToken.None);

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().BeOfType<ObjectDisposedException>();
    }

    [TestMethod]
    public async Task Initialize_SetsUpGpioPinsCorrectly()
    {
        // Act
        await _roofController.Initialize(CancellationToken.None);

        // Assert
        _mockGpioController.IsPinOpen(_options.OpenRoofRelayPin).Should().BeTrue();
        _mockGpioController.IsPinOpen(_options.CloseRoofRelayPin).Should().BeTrue();
        _mockGpioController.IsPinOpen(_options.StopRoofRelayPin).Should().BeTrue();
        _mockGpioController.IsPinOpen(_options.KeypadEnableRelayPin).Should().BeTrue();
        
        // Verify initial relay states after pin setup (final state after initialization)
        _mockGpioController.Read(_options.OpenRoofRelayPin).Should().Be(PinValue.Low);  // RelayOff
        _mockGpioController.Read(_options.CloseRoofRelayPin).Should().Be(PinValue.High); // RelayOn (initial state)
        _mockGpioController.Read(_options.StopRoofRelayPin).Should().Be(PinValue.Low);   // RelayOff
        _mockGpioController.Read(_options.KeypadEnableRelayPin).Should().Be(PinValue.High); // RelayOn
    }

    #endregion

    #region Stop Operation Tests

    [TestMethod]
    public async Task Stop_WhenInitialized_ReturnsSuccess()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        var result = _roofController.Stop();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(_roofController.Status);
    }

    [TestMethod]
    public void Stop_WhenNotInitialized_ReturnsFailure()
    {
        // Act
        var result = _roofController.Stop();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Stop_SetsCorrectRelayStates()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        _roofController.Stop();

        // Assert
        _mockGpioController.Read(_options.StopRoofRelayPin).Should().Be(PinValue.High);
        _mockGpioController.Read(_options.OpenRoofRelayPin).Should().Be(PinValue.Low);
        _mockGpioController.Read(_options.CloseRoofRelayPin).Should().Be(PinValue.Low);
        _mockGpioController.Read(_options.KeypadEnableRelayPin).Should().Be(PinValue.High);
    }

    [TestMethod]
    public async Task Stop_StopsSafetyWatchdog()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        _roofController.Open(); // Start watchdog

        // Act
        _roofController.Stop();

        // Assert - watchdog should be stopped (verified through log messages)
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Safety watchdog stopped")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Open Operation Tests

    [TestMethod]
    public async Task Open_WhenInitialized_ReturnsSuccess()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        var result = _roofController.Open();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Opening);
        _roofController.Status.Should().Be(RoofControllerStatus.Opening);
    }

    [TestMethod]
    public void Open_WhenNotInitialized_ReturnsFailure()
    {
        // Act
        var result = _roofController.Open();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Open_WhenAlreadyOpen_ReturnsSuccessWithOpenStatus()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        
        // Simulate roof already open by triggering open limit switch
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100); // Allow event processing

        // Act
        var result = _roofController.Open();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Open);
        _roofController.Status.Should().Be(RoofControllerStatus.Open);
    }

    [TestMethod]
    public async Task Open_SetsCorrectRelayStates()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        _roofController.Open();

        // Assert
        _mockGpioController.Read(_options.StopRoofRelayPin).Should().Be(PinValue.Low);
        _mockGpioController.Read(_options.OpenRoofRelayPin).Should().Be(PinValue.High);
        _mockGpioController.Read(_options.CloseRoofRelayPin).Should().Be(PinValue.Low);
        _mockGpioController.Read(_options.KeypadEnableRelayPin).Should().Be(PinValue.Low);
    }

    [TestMethod]
    public async Task Open_StartsSafetyWatchdog()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        _roofController.Open();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Safety watchdog started")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Close Operation Tests

    [TestMethod]
    public async Task Close_WhenInitialized_ReturnsSuccess()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        var result = _roofController.Close();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Closing);
        _roofController.Status.Should().Be(RoofControllerStatus.Closing);
    }

    [TestMethod]
    public void Close_WhenNotInitialized_ReturnsFailure()
    {
        // Act
        var result = _roofController.Close();

        // Assert
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Close_WhenAlreadyClosed_ReturnsSuccessWithClosedStatus()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        
        // Simulate roof already closed by triggering closed limit switch
        _mockGpioController.SimulatePinValueChange(_options.RoofClosedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100); // Allow event processing

        // Act
        var result = _roofController.Close();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Closed);
        _roofController.Status.Should().Be(RoofControllerStatus.Closed);
    }

    [TestMethod]
    public async Task Close_SetsCorrectRelayStates()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        _roofController.Close();

        // Assert
        _mockGpioController.Read(_options.StopRoofRelayPin).Should().Be(PinValue.Low);
        _mockGpioController.Read(_options.OpenRoofRelayPin).Should().Be(PinValue.Low);
        _mockGpioController.Read(_options.CloseRoofRelayPin).Should().Be(PinValue.High);
        _mockGpioController.Read(_options.KeypadEnableRelayPin).Should().Be(PinValue.Low);
    }

    [TestMethod]
    public async Task Close_StartsSafetyWatchdog()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        _roofController.Close();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Safety watchdog started")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Limit Switch Tests

    [TestMethod]
    public async Task OpenLimitSwitch_WhenTriggered_StopsRoof()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        _roofController.Open(); // Start opening

        // Act - Simulate open limit switch being triggered (contacted)
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100); // Allow event processing

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Open);
        _mockGpioController.Read(_options.StopRoofRelayPin).Should().Be(PinValue.High);
    }

    [TestMethod]
    public async Task ClosedLimitSwitch_WhenTriggered_StopsRoof()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        _roofController.Close(); // Start closing

        // Act - Simulate closed limit switch being triggered (contacted)
        _mockGpioController.SimulatePinValueChange(_options.RoofClosedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100); // Allow event processing

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Closed);
        _mockGpioController.Read(_options.StopRoofRelayPin).Should().Be(PinValue.High);
    }

    [TestMethod]
    public async Task LimitSwitch_WhenReleased_UpdatesStatusOnly()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        
        // First trigger the limit switch (contact)
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100);

        // Act - Release the limit switch
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.High);
        await Task.Delay(100);

        // Assert - Should not trigger another stop, just update status
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("released")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Status Update Logic Tests

    [TestMethod]
    public async Task UpdateRoofStatus_WithOpenLimitTriggered_SetsOpenStatus()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100);

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Open);
    }

    [TestMethod]
    public async Task UpdateRoofStatus_WithClosedLimitTriggered_SetsClosedStatus()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        _mockGpioController.SimulatePinValueChange(_options.RoofClosedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100);

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Closed);
    }

    [TestMethod]
    public async Task UpdateRoofStatus_WithBothLimitsTriggered_SetsErrorStatus()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act - Trigger both limit switches (hardware error scenario)
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.Low);
        _mockGpioController.SimulatePinValueChange(_options.RoofClosedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100);

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Error);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Both limit switches")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task UpdateRoofStatus_BetweenPositions_UsesLastCommand()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act - Start opening (neither limit switch triggered)
        _roofController.Open();

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Opening);

        // Act - Start closing
        _roofController.Close();

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Closing);
    }

    #endregion

    #region Safety Watchdog Tests

    [TestMethod]
    public async Task SafetyWatchdog_DoesNotTrigger_WhenOperationCompletesQuickly()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        
        // Act - Start opening and then stop quickly
        var openResult = _roofController.Open();
        await Task.Delay(100); // Wait briefly but well under the 2-second timeout
        var stopResult = _roofController.Stop();

        // Assert
        openResult.IsSuccessful.Should().BeTrue();
        stopResult.IsSuccessful.Should().BeTrue();
        _roofController.Status.Should().NotBe(RoofControllerStatus.Error);
    }

    [TestMethod]
    public async Task SafetyWatchdog_TriggersAfterTimeout_WhenOpeningTooLong()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        
        // Act - Start opening and wait for watchdog to trigger
        var openResult = _roofController.Open();
        openResult.IsSuccessful.Should().BeTrue();
        _roofController.Status.Should().Be(RoofControllerStatus.Opening);
        
        // Wait for safety watchdog to trigger (timeout is 2 seconds + some buffer)
        await Task.Delay(3000);

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Error);
        
        // Verify that a warning was logged about the watchdog trigger
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SAFETY WATCHDOG TRIGGERED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task SafetyWatchdog_TriggersAfterTimeout_WhenClosingTooLong()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        
        // Act - Start closing and wait for watchdog to trigger
        var closeResult = _roofController.Close();
        closeResult.IsSuccessful.Should().BeTrue();
        _roofController.Status.Should().Be(RoofControllerStatus.Closing);
        
        // Wait for safety watchdog to trigger (timeout is 2 seconds + some buffer)
        await Task.Delay(3000);

        // Assert
        _roofController.Status.Should().Be(RoofControllerStatus.Error);
        
        // Verify that a warning was logged about the watchdog trigger
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SAFETY WATCHDOG TRIGGERED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task SafetyWatchdog_StoppedByLimitSwitch_DoesNotTrigger()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        
        // Act - Start opening and hit limit switch before timeout
        _roofController.Open();
        await Task.Delay(500); // Wait some time but not full timeout
        
        // Trigger open limit switch
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100);
        
        // Wait past the watchdog timeout
        await Task.Delay(3000);

        // Assert - Should not trigger watchdog since limit switch stopped it
        _roofController.Status.Should().Be(RoofControllerStatus.Open);
        _roofController.Status.Should().NotBe(RoofControllerStatus.Error);
    }

    #endregion

    #region Disposal Tests

    [TestMethod]
    public async Task Dispose_WhenCalled_CleansUpResourcesProperly()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act
        await _roofController.DisposeAsync();

        // Assert - Should not throw and subsequent operations should fail
        var result = _roofController.Open();
        result.IsSuccessful.Should().BeFalse();
        result.Error.Should().BeOfType<ObjectDisposedException>();
    }

    [TestMethod]
    public async Task Dispose_WhenOperationInProgress_StopsOperation()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        _roofController.Open(); // Start operation

        // Act
        await _roofController.DisposeAsync();

        // Assert - Should have stopped the operation during disposal
        // (This is verified by the fact that disposal completes without hanging)
    }

    #endregion

    #region Error Handling Tests

    [TestMethod]
    public void Operations_WhenDisposed_ThrowObjectDisposedException()
    {
        // Arrange
        _roofController.Initialize(CancellationToken.None).Wait();
        _roofController.DisposeAsync().AsTask().Wait();

        // Act & Assert
        var openResult = _roofController.Open();
        openResult.IsSuccessful.Should().BeFalse();
        openResult.Error.Should().BeOfType<ObjectDisposedException>();

        var closeResult = _roofController.Close();
        closeResult.IsSuccessful.Should().BeFalse();
        closeResult.Error.Should().BeOfType<ObjectDisposedException>();

        var stopResult = _roofController.Stop();
        stopResult.IsSuccessful.Should().BeFalse();
        stopResult.Error.Should().BeOfType<ObjectDisposedException>();
    }

    [TestMethod]
    public void Operations_WhenNotInitialized_ReturnFailure()
    {
        // Act & Assert
        var openResult = _roofController.Open();
        openResult.IsSuccessful.Should().BeFalse();
        openResult.Error.Should().BeOfType<InvalidOperationException>();

        var closeResult = _roofController.Close();
        closeResult.IsSuccessful.Should().BeFalse();
        closeResult.Error.Should().BeOfType<InvalidOperationException>();

        var stopResult = _roofController.Stop();
        stopResult.IsSuccessful.Should().BeFalse();
        stopResult.Error.Should().BeOfType<InvalidOperationException>();
    }

    #endregion

    #region Integration Tests

    [TestMethod]
    public async Task FullOpenCloseSequence_WorksCorrectly()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act & Assert - Complete open sequence
        var openResult = _roofController.Open();
        openResult.IsSuccessful.Should().BeTrue();
        _roofController.Status.Should().Be(RoofControllerStatus.Opening);

        // Simulate reaching open limit
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100);
        _roofController.Status.Should().Be(RoofControllerStatus.Open);

        // Act & Assert - Complete close sequence
        var closeResult = _roofController.Close();
        closeResult.IsSuccessful.Should().BeTrue();
        _roofController.Status.Should().Be(RoofControllerStatus.Closing);

        // Reset open limit and trigger closed limit
        _mockGpioController.SimulatePinValueChange(_options.RoofOpenedLimitSwitchPin, PinValue.High);
        _mockGpioController.SimulatePinValueChange(_options.RoofClosedLimitSwitchPin, PinValue.Low);
        await Task.Delay(100);
        _roofController.Status.Should().Be(RoofControllerStatus.Closed);
    }

    [TestMethod]
    public async Task EmergencyStop_DuringOperation_StopsImmediately()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);
        var openResult = _roofController.Open(); // Start opening
        openResult.IsSuccessful.Should().BeTrue();
        _roofController.Status.Should().Be(RoofControllerStatus.Opening);

        // Act
        var stopResult = _roofController.Stop();

        // Assert
        stopResult.IsSuccessful.Should().BeTrue();
        stopResult.Value.Should().Be(RoofControllerStatus.Stopped);
        _roofController.Status.Should().Be(RoofControllerStatus.Stopped);
        _mockGpioController.Read(_options.StopRoofRelayPin).Should().Be(PinValue.High);
    }

    [TestMethod]
    public async Task MultipleOperations_ExecuteSequentially()
    {
        // Arrange
        await _roofController.Initialize(CancellationToken.None);

        // Act - Multiple operations in sequence
        var open1 = _roofController.Open();
        var stop1 = _roofController.Stop();
        var close1 = _roofController.Close();
        var stop2 = _roofController.Stop();
        var open2 = _roofController.Open();

        // Assert - All operations should succeed
        open1.IsSuccessful.Should().BeTrue();
        stop1.IsSuccessful.Should().BeTrue();
        close1.IsSuccessful.Should().BeTrue();
        stop2.IsSuccessful.Should().BeTrue();
        open2.IsSuccessful.Should().BeTrue();
    }

    #endregion

    #region Configuration Tests

    [TestMethod]
    public void SafetyWatchdogTimeout_DefaultValue_Is90Seconds()
    {
        // Arrange
        var defaultOptions = new RoofControllerOptions();

        // Assert
        defaultOptions.SafetyWatchdogTimeout.Should().Be(TimeSpan.FromSeconds(90));
    }

    [TestMethod]
    public async Task CustomWatchdogTimeout_IsRespected()
    {
        // Arrange
        var customOptions = new RoofControllerOptions
        {
            SafetyWatchdogTimeout = TimeSpan.FromMilliseconds(500) // Very short for testing
        };
        var logger = new Mock<ILogger<RoofControllerService>>();
        var gpioController = new MockGpioController();
        var optionsWrapper = Options.Create(customOptions);
        
        using var controller = new RoofControllerService(logger.Object, optionsWrapper, gpioController);
        await controller.Initialize(CancellationToken.None);

        // Act
        controller.Open();
        await Task.Delay(1000); // Wait longer than custom timeout

        // Assert
        controller.Status.Should().Be(RoofControllerStatus.Error);
    }

    #endregion
}
