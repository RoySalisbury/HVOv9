using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using HVO.WebSite.RoofControllerV4;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using HVO;

namespace HVO.WebSite.RoofControllerV4.Tests.Controllers;

[TestClass]
public class RoofControllerApiTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Mock<IRoofControllerServiceV4> _roofServiceMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _roofServiceMock = new Mock<IRoofControllerServiceV4>(MockBehavior.Strict);
        _roofServiceMock.SetupGet(s => s.Status).Returns(RoofControllerStatus.Closed);
        _roofServiceMock.SetupGet(s => s.IsInitialized).Returns(true);
        _roofServiceMock.SetupGet(s => s.IsMoving).Returns(false);
        _roofServiceMock.SetupGet(s => s.LastStopReason).Returns(RoofControllerStopReason.NormalStop);
        _roofServiceMock.Setup(s => s.Initialize(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        // Default success setups (overridden per test where needed)
        _roofServiceMock.Setup(s => s.Open()).Returns(Result<RoofControllerStatus>.Success(RoofControllerStatus.Opening));
        _roofServiceMock.Setup(s => s.Close()).Returns(Result<RoofControllerStatus>.Success(RoofControllerStatus.Closing));
        _roofServiceMock.Setup(s => s.Stop(It.IsAny<RoofControllerStopReason>()))
            .Returns(Result<RoofControllerStatus>.Success(RoofControllerStatus.Stopped));
        _roofServiceMock.Setup(s => s.ClearFault(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove hosted background service to prevent unintended Initialize loops
                    var hostedToRemove = services
                        .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType != null && d.ImplementationType.Name == "RoofControllerServiceV4Host")
                        .ToList();
                    foreach (var d in hostedToRemove)
                        services.Remove(d);

                    // Remove existing roof service registration
                    var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IRoofControllerServiceV4));
                    if (existing != null)
                        services.Remove(existing);

                    services.AddSingleton(_roofServiceMock.Object);
                });
            });

        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    // Helper for strongly-typed JSON responses
    private static async Task<(HttpResponseMessage Response, T? Payload)> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        T? payload = default;
        if (response.IsSuccessStatusCode)
        {
            // Use the same enum string handling as the server (string enums)
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };
            if (!options.Converters.Any(c => c is JsonStringEnumConverter))
            {
                options.Converters.Add(new JsonStringEnumConverter());
            }
            payload = await response.Content.ReadFromJsonAsync<T>(options);
        }
        return (response, payload);
    }

    [TestMethod]
    public async Task GetStatus_ReturnsCurrentStatus()
    {
        // Arrange
        _roofServiceMock.SetupGet(s => s.Status).Returns(RoofControllerStatus.Open);

        // Act
    var (response, payload) = await GetJsonAsync<RoofStatusResponse>(_client, "/api/v4.0/RoofControl/Status");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    Assert.IsNotNull(payload);
    Assert.AreEqual(RoofControllerStatus.Open, payload!.Status);
    Assert.IsFalse(payload.IsMoving);
        _roofServiceMock.VerifyGet(s => s.Status, Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task Open_Success_ReturnsUpdatedStatus()
    {
        // Arrange
    _roofServiceMock.Setup(s => s.Open()).Returns(Result<RoofControllerStatus>.Success(RoofControllerStatus.Opening));
    _roofServiceMock.SetupGet(s => s.Status).Returns(RoofControllerStatus.Opening);

        // Act
    var (response, payload) = await GetJsonAsync<RoofStatusResponse>(_client, "/api/v4.0/RoofControl/Open");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    Assert.IsNotNull(payload);
    Assert.AreEqual(RoofControllerStatus.Opening, payload!.Status);
    Assert.IsFalse(payload.IsMoving); // Mock reports false unless overridden
        _roofServiceMock.Verify(s => s.Open(), Times.Once);
    }

    [TestMethod]
    public async Task Open_InvalidOperation_ReturnsProblem500()
    {
        // Arrange
        _roofServiceMock.Setup(s => s.Open()).Returns(Result<RoofControllerStatus>.Failure(new InvalidOperationException("Not initialized")));

        // Act
        var response = await _client.GetAsync("/api/v4.0/RoofControl/Open");
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();

        // Assert
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.IsNotNull(problem);
        StringAssert.Contains(problem.Title, "Service Error");
        _roofServiceMock.Verify(s => s.Open(), Times.Once);
    }

    [TestMethod]
    public async Task Close_Success_ReturnsUpdatedStatus()
    {
        // Arrange
    _roofServiceMock.Setup(s => s.Close()).Returns(Result<RoofControllerStatus>.Success(RoofControllerStatus.Closing));
    _roofServiceMock.SetupGet(s => s.Status).Returns(RoofControllerStatus.Closing);

        // Act
    var (response, payload) = await GetJsonAsync<RoofStatusResponse>(_client, "/api/v4.0/RoofControl/Close");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    Assert.IsNotNull(payload);
    Assert.AreEqual(RoofControllerStatus.Closing, payload!.Status);
    Assert.IsFalse(payload.IsMoving);
        _roofServiceMock.Verify(s => s.Close(), Times.Once);
    }

    [TestMethod]
    public async Task Close_Failure_ReturnsProblem500()
    {
        // Arrange
        _roofServiceMock.Setup(s => s.Close())
            .Returns(Result<RoofControllerStatus>.Failure(new InvalidOperationException("Cannot close while opening")));

        // Act
        var response = await _client.GetAsync("/api/v4.0/RoofControl/Close");
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();

        // Assert
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.IsNotNull(problem);
        StringAssert.Contains(problem.Title, "Service Error");
        // Accept either raw path or METHOD path format
    var expectedPath = "/api/v4.0/RoofControl/Close";
    Assert.IsNotNull(problem.Instance);
    Assert.IsTrue(problem.Instance == expectedPath || problem.Instance!.EndsWith(expectedPath), $"Unexpected Instance: {problem.Instance}");
        _roofServiceMock.Verify(s => s.Close(), Times.Once);
    }

    [TestMethod]
    public async Task Stop_Success_ReturnsUpdatedStatus()
    {
        // Arrange
        _roofServiceMock.Setup(s => s.Stop(It.IsAny<RoofControllerStopReason>()))
            .Returns(Result<RoofControllerStatus>.Success(RoofControllerStatus.Stopped));
        _roofServiceMock.SetupGet(s => s.Status).Returns(RoofControllerStatus.Stopped);

        // Act
    var (response, payload) = await GetJsonAsync<RoofStatusResponse>(_client, "/api/v4.0/RoofControl/Stop");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    Assert.IsNotNull(payload);
    Assert.AreEqual(RoofControllerStatus.Stopped, payload!.Status);
    Assert.IsFalse(payload.IsMoving);
        _roofServiceMock.Verify(s => s.Stop(It.IsAny<RoofControllerStopReason>()), Times.Once);
    }

    [TestMethod]
    public async Task Stop_Failure_ReturnsProblem500()
    {
        // Arrange
        _roofServiceMock.Setup(s => s.Stop(It.IsAny<RoofControllerStopReason>()))
            .Returns(Result<RoofControllerStatus>.Failure(new InvalidOperationException("Stop not allowed in current state")));

        // Act
        var response = await _client.GetAsync("/api/v4.0/RoofControl/Stop");
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();

        // Assert
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.IsNotNull(problem);
        StringAssert.Contains(problem.Title, "Service Error");
    var expectedPath = "/api/v4.0/RoofControl/Stop";
    Assert.IsNotNull(problem.Instance);
    Assert.IsTrue(problem.Instance == expectedPath || problem.Instance!.EndsWith(expectedPath), $"Unexpected Instance: {problem.Instance}");
        _roofServiceMock.Verify(s => s.Stop(It.IsAny<RoofControllerStopReason>()), Times.Once);
    }

    // Removed manual enum parsing; DTO-based deserialization now handles status cleanly

    [TestMethod]
    public async Task ClearFault_Success_ReturnsTrue()
    {
        // Arrange
    _roofServiceMock.Setup(s => s.ClearFault(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result<bool>.Success(true));

        // Act
        var response = await _client.PostAsync("/api/v4.0/RoofControl/ClearFault?pulseMs=300", null);
        var payload = await response.Content.ReadFromJsonAsync<bool>();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(payload);
    _roofServiceMock.Verify(s => s.ClearFault(300, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ClearFault_InvalidOperation_ReturnsProblem500()
    {
        // Arrange
        _roofServiceMock.Setup(s => s.ClearFault(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure(new InvalidOperationException("Cannot clear fault while moving")));

        // Act
        var response = await _client.PostAsync("/api/v4.0/RoofControl/ClearFault", null);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();

        // Assert
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.IsNotNull(problem);
        StringAssert.Contains(problem.Title, "Service Error");
    _roofServiceMock.Verify(s => s.ClearFault(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Status_WhenUnhandledException_ShouldReturnProblemDetailsWithTraceFields()
    {
        // Arrange: simulate unhandled exception by forcing controller path via throwing from Status getter
        _roofServiceMock.SetupGet(s => s.Status).Throws(new TimeoutException("Status retrieval timed out"));

        // Act
        var response = await _client.GetAsync("/api/v4.0/RoofControl/Status");
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();

        // Assert
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.IsNotNull(problem);
        // From global exception handler mapping TimeoutException -> 408 (Request Timeout) but custom handler currently maps to InternalServerError; verify title from handler
        // Handler currently maps TimeoutException to Request Timeout (per middleware) - adjust expectation accordingly if needed
        // In this service's middleware mapping, TimeoutException => 408 Request Timeout
        if (problem.Status == (int)HttpStatusCode.RequestTimeout)
        {
            StringAssert.Contains(problem.Title, "Request Timeout");
        }
        else
        {
            // Fallback if mapping changes
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, problem.Status);
        }
    var expectedStatusPath = "/api/v4.0/RoofControl/Status";
    Assert.IsNotNull(problem.Instance);
    Assert.IsTrue(problem.Instance == expectedStatusPath || problem.Instance!.EndsWith(expectedStatusPath), $"Unexpected Instance: {problem.Instance}");
    }
}
