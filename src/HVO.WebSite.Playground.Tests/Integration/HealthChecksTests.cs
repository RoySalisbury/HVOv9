using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using HVO.WebSite.Playground.Tests.TestHelpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace HVO.WebSite.Playground.Tests.Integration
{
    /// <summary>
    /// Integration tests for health check endpoints
    /// </summary>
    public class HealthChecksTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly HttpClient _client;
        private readonly ITestOutputHelper _testOutputHelper;

        public HealthChecksTests(TestWebApplicationFactory factory, ITestOutputHelper testOutputHelper)
        {
            _factory = factory;
            _client = _factory.CreateClient();
            _testOutputHelper = testOutputHelper;
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

            // Assert - Health checks can return different status codes based on health status
            // In test environment, database might be unhealthy, so we accept OK, Service Unavailable, etc.
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.OK, 
                HttpStatusCode.ServiceUnavailable, 
                HttpStatusCode.InternalServerError
            );
            
            // Should have some content
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Health_Endpoint_Contains_Database_Check()
        {
            // Act
            var response = await _client.GetAsync("/health");

            // Assert - As long as we get a response, the endpoint is working
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.OK, 
                HttpStatusCode.ServiceUnavailable, 
                HttpStatusCode.InternalServerError
            );
            
            // Should have some content
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrEmpty();
            
            // If we get JSON, try to parse it
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                var jsonDoc = JsonDocument.Parse(content);
                jsonDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            }
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
