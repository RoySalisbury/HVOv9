using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using HVO.WebSite.Playground.Tests.TestHelpers;
using FluentAssertions;

namespace HVO.WebSite.Playground.Tests.Integration
{
    /// <summary>
    /// Integration tests for health check endpoints
    /// </summary>
    public class HealthChecksTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public HealthChecksTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        #region Health Check Endpoint Tests

        [Fact]
        public async Task Health_Endpoint_Returns_Success()
        {
            // Act
            var response = await _client.GetAsync("/health");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Health_Endpoint_Returns_Json_Response()
        {
            // Act
            var response = await _client.GetAsync("/health");
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
            content.Should().NotBeNullOrEmpty();
            
            // Verify it's valid JSON
            var jsonDoc = JsonDocument.Parse(content);
            jsonDoc.RootElement.TryGetProperty("status", out var statusElement).Should().BeTrue();
            jsonDoc.RootElement.TryGetProperty("checks", out var checksElement).Should().BeTrue();
            jsonDoc.RootElement.TryGetProperty("duration", out var durationElement).Should().BeTrue();
        }

        [Fact]
        public async Task Health_Endpoint_Contains_Self_Check()
        {
            // Act
            var response = await _client.GetAsync("/health");
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var jsonDoc = JsonDocument.Parse(content);
            var checks = jsonDoc.RootElement.GetProperty("checks");
            
            var selfCheck = checks.EnumerateArray()
                .FirstOrDefault(check => check.GetProperty("name").GetString() == "self");
            
            selfCheck.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            selfCheck.GetProperty("status").GetString().Should().Be("Healthy");
        }

        [Fact]
        public async Task Health_Endpoint_Contains_Database_Check()
        {
            // Act
            var response = await _client.GetAsync("/health");
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var jsonDoc = JsonDocument.Parse(content);
            var checks = jsonDoc.RootElement.GetProperty("checks");
            
            var dbCheck = checks.EnumerateArray()
                .FirstOrDefault(check => check.GetProperty("name").GetString() == "database");
            
            dbCheck.ValueKind.Should().NotBe(JsonValueKind.Undefined);
            // Note: In tests with mocked services, the database check might not be Healthy
            // but it should be present
        }

        [Fact]
        public async Task Health_Ready_Endpoint_Returns_Success()
        {
            // Act
            var response = await _client.GetAsync("/health/ready");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Health_Live_Endpoint_Returns_Success()
        {
            // Act
            var response = await _client.GetAsync("/health/live");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        #endregion
    }
}
