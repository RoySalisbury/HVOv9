using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO;
using HVO.WebSite.RoofControllerV4.Logic;

namespace HVO.WebSite.RoofControllerV4.Tests.Logic;

[TestClass]public class MockRoofControllerTests
{
    private readonly Mock<ILogger<MockRoofController>> _mockLogger;
    private readonly MockRoofController _mockRoofController;

    public MockRoofControllerTests()
    {
        _mockLogger = new Mock<ILogger<MockRoofController>>();
        _mockRoofController = new MockRoofController(_mockLogger.Object);
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidLogger_CreatesInstance()
    {
        // Arrange & Act
        var controller = new MockRoofController(_mockLogger.Object);

        // Assert
        controller.Should().NotBeNull();
        controller.IsInitialized.Should().BeFalse();
        controller.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new MockRoofController(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Initialize Tests

    [TestMethod]
    public async Task Initialize_Always_ReturnsSuccessAndSetsInitialized()
    {
        // Act
        var result = await _mockRoofController.Initialize(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().BeTrue();
        _mockRoofController.IsInitialized.Should().BeTrue();
    }

    [TestMethod]
    public async Task Initialize_WithCancellationToken_ReturnsSuccess()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();
        
        // Act
        var result = await _mockRoofController.Initialize(cancellationTokenSource.Token);

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().BeTrue();
        _mockRoofController.IsInitialized.Should().BeTrue();
    }

    [TestMethod]
    public async Task Initialize_MultipleCalls_AlwaysReturnsSuccess()
    {
        // Act
        var result1 = await _mockRoofController.Initialize(CancellationToken.None);
        var result2 = await _mockRoofController.Initialize(CancellationToken.None);

        // Assert
        result1.IsSuccessful.Should().BeTrue();
        result2.IsSuccessful.Should().BeTrue();
        _mockRoofController.IsInitialized.Should().BeTrue();
    }

    #endregion

    #region Stop Tests

    [TestMethod]
    public void Stop_Always_ReturnsStoppedStatus()
    {
        // Act
        var result = _mockRoofController.Stop();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Stopped);
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    [TestMethod]
    public void Stop_FromAnyState_SetsStoppedStatus()
    {
        // Arrange
        _mockRoofController.Open(); // Set to Opening
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Opening);

        // Act
        var result = _mockRoofController.Stop();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Stopped);
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    #endregion

    #region Open Tests

    [TestMethod]
    public void Open_Always_ReturnsOpeningStatus()
    {
        // Act
        var result = _mockRoofController.Open();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Opening);
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Opening);
    }

    [TestMethod]
    public void Open_FromAnyState_SetsOpeningStatus()
    {
        // Arrange
        _mockRoofController.Close(); // Set to Closing
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Closing);

        // Act
        var result = _mockRoofController.Open();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Opening);
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Opening);
    }

    #endregion

    #region Close Tests

    [TestMethod]
    public void Close_Always_ReturnsClosingStatus()
    {
        // Act
        var result = _mockRoofController.Close();

        // Assert
        result.Should().NotBeNull();
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Closing);
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Closing);
    }

    [TestMethod]
    public void Close_FromAnyState_SetsClosingStatus()
    {
        // Arrange
        _mockRoofController.Open(); // Set to Opening
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Opening);

        // Act
        var result = _mockRoofController.Close();

        // Assert
        result.IsSuccessful.Should().BeTrue();
        result.Value.Should().Be(RoofControllerStatus.Closing);
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Closing);
    }

    #endregion

    #region State Management Tests

    [TestMethod]
    public void Status_InitialValue_IsStopped()
    {
        // Assert
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    [TestMethod]
    public void IsInitialized_InitialValue_IsFalse()
    {
        // Assert
        _mockRoofController.IsInitialized.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(RoofControllerStatus.Opening)]
    [DataRow(RoofControllerStatus.Closing)]
    [DataRow(RoofControllerStatus.Stopped)]
    public void StatusTransitions_WorkCorrectly(RoofControllerStatus expectedStatus)
    {
        // Act & Assert based on status
        switch (expectedStatus)
        {
            case RoofControllerStatus.Opening:
                var openResult = _mockRoofController.Open();
                openResult.IsSuccessful.Should().BeTrue();
                _mockRoofController.Status.Should().Be(RoofControllerStatus.Opening);
                break;
                
            case RoofControllerStatus.Closing:
                var closeResult = _mockRoofController.Close();
                closeResult.IsSuccessful.Should().BeTrue();
                _mockRoofController.Status.Should().Be(RoofControllerStatus.Closing);
                break;
                
            case RoofControllerStatus.Stopped:
                var stopResult = _mockRoofController.Stop();
                stopResult.IsSuccessful.Should().BeTrue();
                _mockRoofController.Status.Should().Be(RoofControllerStatus.Stopped);
                break;
        }
    }

    #endregion

    #region Sequence Tests

    [TestMethod]
    public void SequentialOperations_WorkCorrectly()
    {
        // Test Open -> Stop sequence
        var openResult = _mockRoofController.Open();
        openResult.IsSuccessful.Should().BeTrue();
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Opening);

        var stopResult = _mockRoofController.Stop();
        stopResult.IsSuccessful.Should().BeTrue();
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Stopped);

        // Test Close -> Stop sequence
        var closeResult = _mockRoofController.Close();
        closeResult.IsSuccessful.Should().BeTrue();
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Closing);

        stopResult = _mockRoofController.Stop();
        stopResult.IsSuccessful.Should().BeTrue();
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    [TestMethod]
    public void Open_ThenClose_ChangesStatus()
    {
        // Act
        var openResult = _mockRoofController.Open();
        var closeResult = _mockRoofController.Close();

        // Assert
        openResult.IsSuccessful.Should().BeTrue();
        closeResult.IsSuccessful.Should().BeTrue();
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Closing);
    }

    [TestMethod]
    public void Close_ThenOpen_ChangesStatus()
    {
        // Act
        var closeResult = _mockRoofController.Close();
        var openResult = _mockRoofController.Open();

        // Assert
        closeResult.IsSuccessful.Should().BeTrue();
        openResult.IsSuccessful.Should().BeTrue();
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Opening);
    }

    #endregion

    #region Logging Tests

    [TestMethod]
    public async Task Initialize_LogsInformation()
    {
        // Act
        await _mockRoofController.Initialize(CancellationToken.None);

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

    [TestMethod]
    public void Stop_LogsInformation()
    {
        // Act
        _mockRoofController.Stop();

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

    [TestMethod]
    public void Open_LogsInformation()
    {
        // Act
        _mockRoofController.Open();

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

    [TestMethod]
    public void Close_LogsInformation()
    {
        // Act
        _mockRoofController.Close();

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

    #region Interface Compliance Tests

    [TestMethod]
    public void MockRoofController_ImplementsIRoofController()
    {
        // Assert
        _mockRoofController.Should().BeAssignableTo<IRoofController>();
    }

    [TestMethod]
    public void MockRoofController_HasAllRequiredProperties()
    {
        // Assert - verify properties exist and have expected initial values
        _mockRoofController.Should().NotBeNull();
        _mockRoofController.IsInitialized.Should().BeFalse();
        _mockRoofController.Status.Should().Be(RoofControllerStatus.Stopped);
    }

    [TestMethod]
    public async Task MockRoofController_HasAllRequiredMethods()
    {
        // Assert - verify methods exist and return expected types
        var initializeResult = await _mockRoofController.Initialize(CancellationToken.None);
        var stopResult = _mockRoofController.Stop();
        var openResult = _mockRoofController.Open();
        var closeResult = _mockRoofController.Close();

        initializeResult.Should().BeOfType<Result<bool>>();
        stopResult.Should().BeOfType<Result<RoofControllerStatus>>();
        openResult.Should().BeOfType<Result<RoofControllerStatus>>();
        closeResult.Should().BeOfType<Result<RoofControllerStatus>>();
    }

    #endregion
}
