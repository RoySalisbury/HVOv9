using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using HVO.WebSite.Playground.Controllers;

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
    public void Get_ShouldReturnOkResult_WithPingResponse()
    {
        // Act
        var result = _controller.Get();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().NotBeNull();
        
        // Verify the response structure contains expected properties
        var responseType = okResult.Value!.GetType();
        responseType.GetProperty("Message").Should().NotBeNull();
        responseType.GetProperty("Version").Should().NotBeNull();
        responseType.GetProperty("Timestamp").Should().NotBeNull();
        responseType.GetProperty("MachineName").Should().NotBeNull();
    }

    [Fact]
    public void Get_ShouldReturnSuccessStatus()
    {
        // Act
        var result = _controller.Get();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value!;
        
        // Use reflection to check the Message property
        var messageProperty = response.GetType().GetProperty("Message");
        messageProperty.Should().NotBeNull();
        var messageValue = messageProperty!.GetValue(response) as string;
        
        messageValue.Should().Be("Pong! API is working perfectly.");
    }

    [Fact]
    public void Get_ShouldReturnCurrentMachineName()
    {
        // Act
        var result = _controller.Get();

        // Assert
        var okResult = result as OkObjectResult;
        var response = okResult!.Value!;
        
        var machineNameProperty = response.GetType().GetProperty("MachineName");
        var machineNameValue = machineNameProperty!.GetValue(response) as string;
        
        machineNameValue.Should().NotBeNullOrEmpty();
        machineNameValue.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void Get_ShouldReturnRecentTimestamp()
    {
        // Arrange
        var beforeCall = DateTime.UtcNow;

        // Act
        var result = _controller.Get();

        // Assert
        var afterCall = DateTime.UtcNow;
        var okResult = result as OkObjectResult;
        var response = okResult!.Value!;
        
        var timestampProperty = response.GetType().GetProperty("Timestamp");
        var timestampValue = (DateTime)timestampProperty!.GetValue(response)!;
        
        timestampValue.Should().BeAfter(beforeCall.AddSeconds(-1));
        timestampValue.Should().BeBefore(afterCall.AddSeconds(1));
    }
}
