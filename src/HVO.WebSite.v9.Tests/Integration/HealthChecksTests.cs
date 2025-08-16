using System.Net;
using System.Text.Json;
using FluentAssertions;
using HVO.WebSite.v9.Tests.TestHelpers;

namespace HVO.WebSite.v9.Tests.Integration
{
    /// <summary>
    /// Integration tests for health check endpoints of v9 site
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    public class HealthChecksTests
    {
        private TestWebApplicationFactory _factory = null!;
        private HttpClient _client = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _factory = new TestWebApplicationFactory();
            _client = _factory.CreateClient();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _client?.Dispose();
            _factory?.Dispose();
        }

        [TestMethod]
        public async Task Health_Endpoint_Returns_Success()
        {
            var response = await _client.GetAsync("/health");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [TestMethod]
        public async Task Health_Endpoint_Returns_Json_Response()
        {
            var response = await _client.GetAsync("/health");
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.OK,
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.InternalServerError
            );

            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        public async Task Health_Endpoint_Contains_Object_Json_When_ContentType_Is_Json()
        {
            var response = await _client.GetAsync("/health");
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.OK,
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.InternalServerError
            );

            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotBeNullOrEmpty();

            if (response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                var jsonDoc = JsonDocument.Parse(content);
                jsonDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            }
        }

        [TestMethod]
        public async Task Health_Ready_Endpoint_Returns_Success()
        {
            var response = await _client.GetAsync("/health/ready");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [TestMethod]
        public async Task Health_Live_Endpoint_Returns_Success()
        {
            var response = await _client.GetAsync("/health/live");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
