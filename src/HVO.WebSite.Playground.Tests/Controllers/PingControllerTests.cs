using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using HVO.WebSite.Playground.Controllers;
using HVO.WebSite.Playground.Models;

namespace HVO.WebSite.Playground.Tests.Controllers;

/// <summary>
/// Unit tests for PingController API endpoints
/// </summary>
public class PingControllerTests
{
    private readonly Mock<ILogger<PingController>> _loggerMock;
    private readonly PingController _controller;

    public PingControllerTests()
    {
        _loggerMock = new Mock<ILogger<PingController>>();
        _controller = new PingController(_loggerMock.Object);
    }

    [Fact]
    public void HealthCheck_ShouldReturnOkResult_WithPingResponse()
    {
        // Act
        var result = _controller.HealthCheck();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().NotBeNull();
        okResult.Value.Should().BeOfType<PingResponse>();
        
        // Verify the response contains expected properties
        var response = okResult.Value as PingResponse;
        response!.Message.Should().NotBeNullOrEmpty();
        response.Version.Should().NotBeNullOrEmpty();
        response.MachineName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void HealthCheck_ShouldReturnSuccessStatus()
    {
        // Act
        var result = _controller.HealthCheck();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as PingResponse;
        
        response!.Message.Should().Be("Pong! API is working perfectly.");
    }

    [Fact]
    public void HealthCheck_ShouldReturnCurrentMachineName()
    {
        // Act
        var result = _controller.HealthCheck();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as PingResponse;
        
        response!.MachineName.Should().NotBeNullOrEmpty();
        response.MachineName.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void HealthCheck_ShouldReturnRecentTimestamp()
    {
        // Arrange
        var beforeCall = DateTime.UtcNow;

        // Act
        var result = _controller.HealthCheck();

        // Assert
        var afterCall = DateTime.UtcNow;
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as PingResponse;
        
        response!.Timestamp.Should().BeAfter(beforeCall.AddSeconds(-1));
        response.Timestamp.Should().BeBefore(afterCall.AddSeconds(1));
    }
}
