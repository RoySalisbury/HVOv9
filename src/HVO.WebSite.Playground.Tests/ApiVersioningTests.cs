using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using HVO.WebSite.Playground;

namespace HVO.WebSite.Playground.Tests;

public class ApiVersioningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiVersioningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetPing_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/ping");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
        Assert.Contains("Pong", content);
        Assert.Contains("1.0", content);
    }

    [Fact]
    public async Task GetPing_WithUrlSegmentVersioning_ReturnsCorrectJsonStructure()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/ping");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;
        
        Assert.True(root.TryGetProperty("message", out var messageElement));
        Assert.Equal("Pong! API is working perfectly.", messageElement.GetString());
        
        Assert.True(root.TryGetProperty("version", out var versionElement));
        Assert.Equal("1.0", versionElement.GetString());
        
        Assert.True(root.TryGetProperty("timestamp", out var timestampElement));
        Assert.True(DateTime.TryParse(timestampElement.GetString(), out _));
    }

    [Fact]
    public async Task GetPing_WithInvalidVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v2.0/ping");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPing_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/ping");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
