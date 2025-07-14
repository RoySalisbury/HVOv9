using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace HVO.WebSite.Playground.Tests.Integration;

/// <summary>
/// Integration tests for MVC views and pages
/// </summary>
public class MvcViewIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public MvcViewIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Get_HomeIndex_ShouldReturnSuccessAndCorrectContentType()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.ToString().Should().Contain("text/html");
    }

    [Fact]
    public async Task Get_HomeIndex_ShouldContainExpectedContent()
    {
        // Act
        var response = await _client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("Welcome");
        content.Should().Contain("HVO");
        content.Should().Contain("Playground");
    }
    

    [Fact]
    public async Task Get_HomeHealthCheckMVC_ShouldContainExpectedContent()
    {
        // Act
        var response = await _client.GetAsync("/Home/HealthCheckMVC");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("Health Check API Test");
        content.Should().Contain("MVC");
        content.Should().Contain("JavaScript");
    }

    [Fact]
    public async Task Get_BlazorHome_ShouldReturnSuccessAndCorrectContentType()
    {
        // Act
        var response = await _client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.ToString().Should().Contain("text/html");
    }

    [Fact]
    public async Task Get_BlazorHealthCheckTest_ShouldReturnSuccessAndCorrectContentType()
    {
        // Act
        var response = await _client.GetAsync("/health-check-blazor");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.ToString().Should().Contain("text/html");
    }

    [Fact]
    public async Task Get_BlazorHealthCheckTest_ShouldContainExpectedContent()
    {
        // Act
        var response = await _client.GetAsync("/health-check-blazor");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("Health Check API Test");
        content.Should().Contain("Blazor");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Home/HealthCheckMVC")]
    [InlineData("/health-check-blazor")]
    public async Task Get_AllPages_ShouldHaveValidHtmlStructure(string url)
    {
        // Act
        var response = await _client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("<!DOCTYPE html>");
        content.Should().Contain("<html");
        content.Should().Contain("<head>");
        content.Should().Contain("<body>");
        content.Should().Contain("</html>");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Home/HealthCheckMVC")]
    [InlineData("/health-check-blazor")]
    public async Task Get_AllPages_ShouldHaveBootstrapStyling(string url)
    {
        // Act
        var response = await _client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        content.Should().Contain("bootstrap");
    }

    [Fact]
    public async Task Get_NonExistentPage_ShouldReturn404()
    {
        // Act
        var response = await _client.GetAsync("/NonExistent/Page");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
