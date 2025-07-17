using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;
using HVO.WebSite.RoofControllerV4.Models;
using HVO.WebSite.RoofControllerV4.Services;

namespace HVO.WebSite.RoofControllerV4.Tests.Services;

public class WeatherServiceTests
{
    private readonly Mock<HttpMessageHandler> _mockMessageHandler;
    private readonly Mock<ILogger<WeatherService>> _mockLogger;
    private readonly HttpClient _httpClient;
    private readonly WeatherService _weatherService;

    public WeatherServiceTests()
    {
        _mockMessageHandler = new Mock<HttpMessageHandler>();
        _mockLogger = new Mock<ILogger<WeatherService>>();
        _httpClient = new HttpClient(_mockMessageHandler.Object);
        _weatherService = new WeatherService(_httpClient, _mockLogger.Object);
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ReturnsWeatherData_WhenApiSucceeds()
    {
        // Arrange
        var apiResponse = new CurrentWeatherApiResponse
        {
            Timestamp = DateTime.Now,
            MachineName = "Test-Machine",
            Current = new CurrentWeatherApiData
            {
                RecordDateTime = DateTimeOffset.Now,
                OutsideTemperature = 72.5m,
                InsideTemperature = 70.0m,
                OutsideHumidity = 45,
                InsideHumidity = 42,
                WindSpeed = 8,
                WindDirection = 180,
                Barometer = 29.92m,
                RainRate = 0.0m,
                DailyRainAmount = 0.25m,
                UvIndex = 5,
                SolarRadiation = 650,
                SunriseTime = new TimeOnly(6, 30),
                SunsetTime = new TimeOnly(19, 45)
            },
            TodaysExtremes = new TodaysExtremesApiData
            {
                OutsideTemperature = new TemperatureExtremesApi
                {
                    High = 85.0m,
                    Low = 65.0m,
                    HighTime = DateTimeOffset.Now.AddHours(-2),
                    LowTime = DateTimeOffset.Now.AddHours(-12)
                },
                OutsideHumidity = new HumidityExtremesApi
                {
                    High = 60,
                    Low = 30
                },
                WindSpeed = new WindSpeedExtremesApi
                {
                    High = 15
                },
                Barometer = new BarometerExtremesApi
                {
                    High = 30.10m,
                    Low = 29.80m
                }
            }
        };

        var jsonResponse = JsonSerializer.Serialize(apiResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        _mockMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _weatherService.GetCurrentWeatherAsync();

        // Assert
        result.Should().NotBeNull();
        result.OutsideTemperature.Should().Be(72.5m);
        result.InsideTemperature.Should().Be(70.0m);
        result.OutsideHumidity.Should().Be(45);
        result.InsideHumidity.Should().Be(42);
        result.WindSpeed.Should().Be(8);
        result.WindDirection.Should().Be(180);
        result.Barometer.Should().Be(29.92m);
        result.RainRate.Should().Be(0.0m);
        result.DailyRainAmount.Should().Be(0.25m);
        result.UvIndex.Should().Be(5);
        result.SolarRadiation.Should().Be(650);
        result.SunriseTime.Should().Be(new TimeOnly(6, 30));
        result.SunsetTime.Should().Be(new TimeOnly(19, 45));
        result.WeatherCondition.Should().Be("Sunny");
        result.WindDirectionText.Should().Be("S");
        result.TodaysHighTemp.Should().Be(85.0m);
        result.TodaysLowTemp.Should().Be(65.0m);
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ReturnsFallbackData_WhenApiReturnsError()
    {
        // Arrange
        var httpResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);

        _mockMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _weatherService.GetCurrentWeatherAsync();

        // Assert
        result.Should().NotBeNull();
        result.WeatherCondition.Should().Be("API Unavailable");
        result.OutsideTemperature.Should().Be(75.0m);
        result.InsideTemperature.Should().Be(72.0m);
        result.OutsideHumidity.Should().Be(45);
        result.InsideHumidity.Should().Be(40);
        result.WindSpeed.Should().Be(5);
        result.WindDirection.Should().Be(180);
        result.Barometer.Should().Be(29.92m);
        result.RainRate.Should().Be(0);
        result.DailyRainAmount.Should().Be(0);
        result.UvIndex.Should().Be(3);
        result.SolarRadiation.Should().Be(400);
        result.SunriseTime.Should().Be(new TimeOnly(6, 30));
        result.SunsetTime.Should().Be(new TimeOnly(19, 45));
        result.WindDirectionText.Should().Be("S");
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ReturnsFallbackData_WhenApiThrowsException()
    {
        // Arrange
        _mockMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _weatherService.GetCurrentWeatherAsync();

        // Assert
        result.Should().NotBeNull();
        result.WeatherCondition.Should().Be("API Unavailable");
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ReturnsFallbackData_WhenApiReturnsNullData()
    {
        // Arrange
        var apiResponse = new CurrentWeatherApiResponse
        {
            Timestamp = DateTime.Now,
            MachineName = "Test-Machine",
            Current = null! // Null current data
        };

        var jsonResponse = JsonSerializer.Serialize(apiResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        _mockMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _weatherService.GetCurrentWeatherAsync();

        // Assert
        result.Should().NotBeNull();
        result.WeatherCondition.Should().Be("API Unavailable");
    }

    [Theory]
    [InlineData(0, "Cloudy")]
    [InlineData(100, "Cloudy")]
    [InlineData(200, "Cloudy")]
    [InlineData(201, "Partly Cloudy")]
    [InlineData(300, "Partly Cloudy")]
    [InlineData(500, "Partly Cloudy")]
    [InlineData(501, "Sunny")]
    [InlineData(800, "Sunny")]
    public async Task GetCurrentWeatherAsync_MapsWeatherConditionCorrectly(decimal solarRadiation, string expectedCondition)
    {
        // Arrange
        var apiResponse = new CurrentWeatherApiResponse
        {
            Timestamp = DateTime.Now,
            MachineName = "Test-Machine",
            Current = new CurrentWeatherApiData
            {
                RecordDateTime = DateTimeOffset.Now,
                SolarRadiation = solarRadiation,
                RainRate = 0 // No rain
            }
        };

        var jsonResponse = JsonSerializer.Serialize(apiResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        _mockMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _weatherService.GetCurrentWeatherAsync();

        // Assert
        result.WeatherCondition.Should().Be(expectedCondition);
    }

    [Fact]
    public async Task GetCurrentWeatherAsync_ReturnsRainyCondition_WhenRainRateIsPositive()
    {
        // Arrange
        var apiResponse = new CurrentWeatherApiResponse
        {
            Timestamp = DateTime.Now,
            MachineName = "Test-Machine",
            Current = new CurrentWeatherApiData
            {
                RecordDateTime = DateTimeOffset.Now,
                SolarRadiation = 800, // High solar radiation
                RainRate = 0.5m // But it's raining
            }
        };

        var jsonResponse = JsonSerializer.Serialize(apiResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        _mockMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _weatherService.GetCurrentWeatherAsync();

        // Assert
        result.WeatherCondition.Should().Be("Rainy");
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(22.5, "NNE")]
    [InlineData(45, "NE")]
    [InlineData(67.5, "ENE")]
    [InlineData(90, "E")]
    [InlineData(180, "S")]
    [InlineData(270, "W")]
    [InlineData(360, "N")]
    public async Task GetCurrentWeatherAsync_MapsWindDirectionCorrectly(decimal windDirection, string expectedDirection)
    {
        // Arrange
        var apiResponse = new CurrentWeatherApiResponse
        {
            Timestamp = DateTime.Now,
            MachineName = "Test-Machine",
            Current = new CurrentWeatherApiData
            {
                RecordDateTime = DateTimeOffset.Now,
                WindDirection = windDirection
            }
        };

        var jsonResponse = JsonSerializer.Serialize(apiResponse);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        _mockMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _weatherService.GetCurrentWeatherAsync();

        // Assert
        result.WindDirectionText.Should().Be(expectedDirection);
    }

    private void Dispose()
    {
        _httpClient?.Dispose();
    }
}
