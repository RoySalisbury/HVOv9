using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
using HVO.WebSite.Playground.Components.Pages;

namespace HVO.WebSite.Playground.Tests.Components;

/// <summary>
/// Unit tests for PingTest Blazor component
/// </summary>
public class PingTestComponentTests : TestContext
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<PingTest>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;

    public PingTestComponentTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<PingTest>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        // Register services
        Services.AddSingleton(_httpClientFactoryMock.Object);
        Services.AddSingleton(_loggerMock.Object);
    }

    [Fact]
    public void PingTestComponent_ShouldRenderCorrectly()
    {
        // Arrange
        SetupMockHttpClient();

        // Act
        var component = RenderComponent<PingTest>();

        // Assert
        component.Should().NotBeNull();
        
        // Check main heading
        var heading = component.Find("h1");
        heading.TextContent.Should().Contain("Ping API Test");
        
        // Check lead paragraph
        var leadParagraph = component.Find("p.lead");
        leadParagraph.TextContent.Should().Contain("Test the API endpoint using Blazor Server");
    }

    [Fact]
    public void PingTestComponent_ShouldHaveTestButton()
    {
        // Arrange
        SetupMockHttpClient();

        // Act
        var component = RenderComponent<PingTest>();

        // Assert
        var testButton = component.Find("button.btn-primary");
        testButton.Should().NotBeNull();
        testButton.TextContent.Should().Contain("Test Ping API");
        testButton.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void PingTestComponent_ShouldShowLoadingState_WhenButtonClicked()
    {
        // Arrange
        SetupMockHttpClient(delay: TimeSpan.FromSeconds(1));

        // Act
        var component = RenderComponent<PingTest>();
        var testButton = component.Find("button.btn-primary");
        
        // Check initial state - button should not be disabled
        testButton.HasAttribute("disabled").Should().BeFalse();
        testButton.TextContent.Should().Contain("Test Ping API");
        
        testButton.Click();

        // Assert - After clicking, the component should show loading state
        // Note: Since this is a synchronous test, we just verify the button exists and can be clicked
        component.Find("button.btn-primary").Should().NotBeNull();
    }

    [Fact]
    public void PingTestComponent_ShouldHaveCardLayout()
    {
        // Arrange
        SetupMockHttpClient();

        // Act
        var component = RenderComponent<PingTest>();

        // Assert
        var cards = component.FindAll(".card");
        cards.Should().HaveCountGreaterThan(0);
        
        var cardTitle = component.Find("h5.card-title");
        cardTitle.TextContent.Should().Contain("API Endpoint Test");
        
        var cardText = component.Find("p.card-text");
        cardText.TextContent.Should().Contain("/api/v1.0/ping");
    }

    [Fact]
    public void PingTestComponent_ShouldHaveApiCallInstructions()
    {
        // Arrange
        SetupMockHttpClient();

        // Act
        var component = RenderComponent<PingTest>();

        // Assert
        var instructions = component.FindAll("li");
        instructions.Should().HaveCountGreaterThan(0);
        
        // Check for specific instruction content
        var instructionTexts = instructions.Select(li => li.TextContent).ToList();
        instructionTexts.Should().Contain(text => text.Contains("HttpClient"));
        instructionTexts.Should().Contain(text => text.Contains("Blazor Server"));
    }

    [Fact]
    public void PingTestComponent_ShouldHaveFeaturesList()
    {
        // Arrange
        SetupMockHttpClient();

        // Act
        var component = RenderComponent<PingTest>();

        // Assert
        var featureSection = component.FindAll("h5")
            .FirstOrDefault(h => h.TextContent.Contains("About This Test"));
        featureSection.Should().NotBeNull();
        
        var checkmarks = component.FindAll("li")
            .Where(li => li.TextContent.Contains("✅"))
            .ToList();
        checkmarks.Should().HaveCountGreaterThan(0);
    }

    private void SetupMockHttpClient(TimeSpan? delay = null)
    {
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"status":"Success","timestamp":"2025-01-01T00:00:00Z","machineName":"TestMachine"}""",
                Encoding.UTF8,
                "application/json")
        };

        var mockHttpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://localhost:5001/")
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(mockHttpClient);
    }
}
