using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO;
using HVO.WebSite.RoofControllerV4.Logic;

namespace HVO.WebSite.RoofControllerV4.Tests.Logic;

/// <summary>
/// Tests for IRoofControllerService interface implementations.
/// Since RoofController has complex GPIO dependencies, we focus on testing
/// the MockRoofController which implements the same interface contract.
/// </summary>
[TestClass]public class RoofControllerInterfaceTests : IDisposable
{
    private readonly Mock<ILogger<MockRoofController>> _mockLogger;
    private MockRoofController? _roofController;

    public RoofControllerInterfaceTests()
    {
        _mockLogger = new Mock<ILogger<MockRoofController>>();
    }

    public void Dispose()
    {
        _roofController = null;
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidLogger_CreatesInstance()
    {
        // Act
        _roofController = new MockRoofController(_mockLogger.Object);

        // Assert
        _roofController.Should().NotBeNull();
        _roofController.Status.Should().Be(RoofControllerStatus.Stopped);
        _roofController.IsInitialized.Should().BeFalse();
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new MockRoofController(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Initialization Tests

    [TestMethod]
    public async Task Initialize_WhenNotInitialized_ReturnsSuccessAndSetsInitialized()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        var result = await _roofController.Initialize(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().BeTrue();
        _roofController.IsInitialized.Should().BeTrue();
    }

    [TestMethod]
    public async Task Initialize_LogsInitializationMessage()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        await _roofController.Initialize(CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Initialize called")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Stop Operation Tests

    [TestMethod]
    public void Stop_ReturnsSuccessWithStoppedStatus()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        var result = _roofController.Stop();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Stopped);
        _roofController.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    [TestMethod]
    public void Stop_LogsStopMessage()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        _roofController.Stop();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Stop called")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Open Operation Tests

    [TestMethod]
    public void Open_ReturnsSuccessWithOpeningStatus()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        var result = _roofController.Open();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Opening);
        _roofController.Status.Should().Be(RoofControllerStatus.Opening);
    }

    [TestMethod]
    public void Open_LogsOpenMessage()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        _roofController.Open();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Open called")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Close Operation Tests

    [TestMethod]
    public void Close_ReturnsSuccessWithClosingStatus()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        var result = _roofController.Close();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Closing);
        _roofController.Status.Should().Be(RoofControllerStatus.Closing);
    }

    [TestMethod]
    public void Close_LogsCloseMessage()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        _roofController.Close();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Close called")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region State Management Tests

    [TestMethod]
    [DataRow(RoofControllerStatus.Stopped)]
    [DataRow(RoofControllerStatus.Opening)]
    [DataRow(RoofControllerStatus.Closing)]
    public void StatusProperty_ReflectsCurrentState(RoofControllerStatus expectedStatus)
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act & Assert based on the expected status
        switch (expectedStatus)
        {
            case RoofControllerStatus.Stopped:
                _roofController.Stop();
                _roofController.Status.Should().Be(RoofControllerStatus.Stopped);
                break;
            case RoofControllerStatus.Opening:
                _roofController.Open();
                _roofController.Status.Should().Be(RoofControllerStatus.Opening);
                break;
            case RoofControllerStatus.Closing:
                _roofController.Close();
                _roofController.Status.Should().Be(RoofControllerStatus.Closing);
                break;
        }
    }

    #endregion

    #region Interface Compliance Tests

    [TestMethod]
    public void MockRoofController_ImplementsIRoofControllerServiceInterface()
    {
        // Arrange & Act
        _roofController = new MockRoofController(_mockLogger.Object);

        // Assert
        _roofController.Should().BeAssignableTo<IRoofControllerService>();
    }

    [TestMethod]
    public void IRoofControllerServiceInterface_HasAllRequiredMembers()
    {
        // Arrange
        var interfaceType = typeof(IRoofControllerService);

        // Act & Assert
        interfaceType.GetProperty("IsInitialized").Should().NotBeNull();
        interfaceType.GetProperty("Status").Should().NotBeNull();
        interfaceType.GetMethod("Initialize").Should().NotBeNull();
        interfaceType.GetMethod("Stop").Should().NotBeNull();
        interfaceType.GetMethod("Open").Should().NotBeNull();
        interfaceType.GetMethod("Close").Should().NotBeNull();
    }

    #endregion

    #region Sequential Operations Tests

    [TestMethod]
    public async Task SequentialOperations_WorkCorrectly()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act & Assert - Test initialization
        var initResult = await _roofController.Initialize(CancellationToken.None);
        initResult.IsSuccessful.Should().BeTrue();
        _roofController.IsInitialized.Should().BeTrue();

        // Act & Assert - Test open operation
        var openResult = _roofController.Open();
        openResult.IsSuccessful.Should().BeTrue();
        openResult.Value.Should().Be(RoofControllerStatus.Opening);

        // Act & Assert - Test stop operation
        var stopResult = _roofController.Stop();
        stopResult.IsSuccessful.Should().BeTrue();
        stopResult.Value.Should().Be(RoofControllerStatus.Stopped);

        // Act & Assert - Test close operation
        var closeResult = _roofController.Close();
        closeResult.IsSuccessful.Should().BeTrue();
        closeResult.Value.Should().Be(RoofControllerStatus.Closing);
    }

    #endregion

    #region Logging Verification Tests

    [TestMethod]
    public void AllOperations_LogAppropriateMessages()
    {
        // Arrange
        _roofController = new MockRoofController(_mockLogger.Object);

        // Act
        _roofController.Stop();
        _roofController.Open();
        _roofController.Close();

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Stop called")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Open called")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Close called")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}
