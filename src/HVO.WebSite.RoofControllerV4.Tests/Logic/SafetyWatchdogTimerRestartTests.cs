using FluentAssertions;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO;
using HVO.Iot.Devices.Abstractions;
using HVO.Iot.Devices.Implementation;
using System.Device.Gpio;
using HVO.Iot.Devices;

namespace HVO.WebSite.RoofControllerV4.Tests.Logic;

/// <summary>
/// Tests specifically for verifying the safety watchdog timer restart functionality.
/// These tests ensure that the timer can be restarted multiple times after being triggered.
/// </summary>
[TestClass]
public class SafetyWatchdogTimerRestartTests
{
    private Mock<ILogger<RoofControllerService>> _mockLogger = null!;
    private MockGpioController _mockGpioController = null!;
    private IOptions<RoofControllerOptions> _options = null!;
    private RoofControllerOptions _roofControllerOptions = null!;
    private RoofControllerService _roofController = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<RoofControllerService>>();
        _mockGpioController = new MockGpioController();

        // Use a very short timeout for testing timer restart behavior
        _roofControllerOptions = new RoofControllerOptions
        {
            SafetyWatchdogTimeout = TimeSpan.FromMilliseconds(100), // Very short for quick testing
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
        _options = Options.Create(_roofControllerOptions);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_roofController != null)
        {
            await _roofController.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task SafetyWatchdog_CanRestartAfterFirstTrigger_WhenOpeningMultipleTimes()
    {
        // Arrange
        _roofController = new RoofControllerService(_mockLogger.Object, _options, _mockGpioController);
        await _roofController.Initialize(CancellationToken.None);

        // Act - First open operation - let the watchdog trigger
        var firstOpenResult = _roofController.Open();
        firstOpenResult.IsSuccessful.Should().BeTrue();
        firstOpenResult.Value.Should().Be(RoofControllerStatus.Opening);

        // Wait for safety watchdog to trigger (timeout is 100ms + buffer)
        await Task.Delay(200);

        // Verify the first watchdog triggered
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SAFETY WATCHDOG TRIGGERED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Act - Second open operation - the timer should be able to restart
        var secondOpenResult = _roofController.Open();
        secondOpenResult.IsSuccessful.Should().BeTrue();
        secondOpenResult.Value.Should().Be(RoofControllerStatus.Opening);

        // Wait for the second safety watchdog to trigger
        await Task.Delay(200);

        // Assert - The watchdog should have triggered a second time
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SAFETY WATCHDOG TRIGGERED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2)); // Should have triggered twice
    }

    [TestMethod]
    public async Task SafetyWatchdog_CanRestartAfterFirstTrigger_WhenClosingMultipleTimes()
    {
        // Arrange
        _roofController = new RoofControllerService(_mockLogger.Object, _options, _mockGpioController);
        await _roofController.Initialize(CancellationToken.None);

        // Act - First close operation - let the watchdog trigger
        var firstCloseResult = _roofController.Close();
        firstCloseResult.IsSuccessful.Should().BeTrue();
        firstCloseResult.Value.Should().Be(RoofControllerStatus.Closing);

        // Wait for safety watchdog to trigger (timeout is 100ms + buffer)
        await Task.Delay(200);

        // Verify the first watchdog triggered
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SAFETY WATCHDOG TRIGGERED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Act - Second close operation - the timer should be able to restart
        var secondCloseResult = _roofController.Close();
        secondCloseResult.IsSuccessful.Should().BeTrue();
        secondCloseResult.Value.Should().Be(RoofControllerStatus.Closing);

        // Wait for the second safety watchdog to trigger
        await Task.Delay(200);

        // Assert - The watchdog should have triggered a second time
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SAFETY WATCHDOG TRIGGERED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2)); // Should have triggered twice
    }

    [TestMethod]
    public async Task SafetyWatchdog_StartsWithCorrectTimeout_AfterMultipleOperations()
    {
        // Arrange
        _roofController = new RoofControllerService(_mockLogger.Object, _options, _mockGpioController);
        await _roofController.Initialize(CancellationToken.None);

        // Act - Perform multiple operations and verify the timer starts each time
        for (int i = 1; i <= 3; i++)
        {
            var openResult = _roofController.Open();
            openResult.IsSuccessful.Should().BeTrue();

            // Immediately stop to prevent watchdog from triggering
            var stopResult = _roofController.Stop();
            stopResult.IsSuccessful.Should().BeTrue();

            // Verify each start logs the correct timeout duration
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Safety watchdog started for 0.1 seconds")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeast(i));
        }
    }
}
