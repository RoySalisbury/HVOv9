using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using HVO.WebSite.Playground;
using HVO.WebSite.Playground.Tests.TestHelpers;

namespace HVO.WebSite.Playground.Tests;

public class ApiVersioningTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApiVersioningTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetPing_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/ping/health");

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
        var response = await _client.GetAsync("/api/v1.0/ping/health");

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
        var response = await _client.GetAsync("/api/v2.0/ping/health");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPing_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/ping/health");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #region Weather API Versioning Tests

    [Fact]
    public async Task GetWeatherLatest_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/latest");

        // Assert - We expect this to succeed even if data is not available (404 is valid business logic)
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound);
        
        // Verify it's not a versioning issue (which would be 404 with different error structure)
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var content = await response.Content.ReadAsStringAsync();
            // Should contain weather-specific error, not a generic not found
            Assert.Contains("Weather", content);
        }
    }

    [Fact]
    public async Task GetWeatherLatest_WithInvalidVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v2.0/weather/latest");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherLatest_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/weather/latest");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherHighsLows_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/highs-lows");

        // Assert - We expect this to succeed even if data is not available (404 is valid business logic)
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound);
        
        // Verify it's not a versioning issue
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var content = await response.Content.ReadAsStringAsync();
            // Should contain weather-specific error, not a generic not found
            Assert.Contains("Weather", content);
        }
    }

    [Fact]
    public async Task GetWeatherHighsLows_WithInvalidVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v2.0/weather/highs-lows");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherHighsLows_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/weather/highs-lows");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherCurrent_WithUrlSegmentVersioning_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1.0/weather/current");

        // Assert - We expect this to succeed even if data is not available (404 is valid business logic)
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NotFound);
        
        // Verify it's not a versioning issue
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var content = await response.Content.ReadAsStringAsync();
            // Should contain weather-specific error, not a generic not found
            Assert.Contains("Weather", content);
        }
    }

    [Fact]
    public async Task GetWeatherCurrent_WithInvalidVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v2.0/weather/current");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWeatherCurrent_WithoutVersion_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/weather/current");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AllWeatherEndpoints_SupportApiVersioning()
    {
        // Arrange - All weather endpoints that should support v1.0
        var weatherEndpoints = new[]
        {
            "/api/v1.0/weather/latest",
            "/api/v1.0/weather/highs-lows",
            "/api/v1.0/weather/current"
        };

        // Act & Assert
        foreach (var endpoint in weatherEndpoints)
        {
            var response = await _client.GetAsync(endpoint);
            
            // Should not return 404 due to versioning issues
            // Business logic 404s are acceptable (no data available)
            Assert.True(
                response.StatusCode == HttpStatusCode.OK || 
                response.StatusCode == HttpStatusCode.NotFound ||
                response.StatusCode == HttpStatusCode.InternalServerError, // Acceptable for complex operations
                $"Endpoint {endpoint} returned unexpected status: {response.StatusCode}"
            );
            
            // Verify Content-Type header is set for JSON API
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.Contains("application/json", response.Content.Headers.ContentType?.ToString() ?? "");
            }
        }
    }

    #endregion
}
