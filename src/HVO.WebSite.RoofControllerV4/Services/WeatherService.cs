using HVO.WebSite.RoofControllerV4.Models;

namespace HVO.WebSite.RoofControllerV4.Services;

/// <summary>
/// Service interface for weather operations
/// </summary>
public interface IWeatherService
{
    /// <summary>
    /// Gets the current weather conditions
    /// </summary>
    /// <returns>Current weather data</returns>
    Task<WeatherData> GetCurrentWeatherAsync();
}

/// <summary>
/// Simple weather service implementation that provides mock data
/// In a real implementation, this would connect to actual weather station hardware
/// </summary>
public class WeatherService : IWeatherService
{
    private readonly Random _random = new();
    
    public async Task<WeatherData> GetCurrentWeatherAsync()
    {
        // Simulate async operation
        await Task.Delay(100);
        
        // Generate realistic weather data
        var baseTemp = 72.0m + ((decimal)_random.NextSingle() - 0.5m) * 20; // 62-82°F range
        var humidity = (byte)(45 + _random.Next(0, 30)); // 45-75% range
        var windSpeed = (byte)_random.Next(0, 15); // 0-15 mph
        var windDirection = (short)_random.Next(0, 360);
        var barometer = 29.92m + ((decimal)_random.NextSingle() - 0.5m) * 0.5m; // 29.67-30.17 inHg
        var rainRate = _random.NextSingle() < 0.1 ? (decimal)(_random.NextSingle() * 0.5) : 0; // 10% chance of rain
        var dailyRain = rainRate > 0 ? (decimal)(_random.NextSingle() * 2) : 0;
        var uvIndex = (byte)_random.Next(0, 11);
        var solarRadiation = (short)_random.Next(0, 1000);
        
        return new WeatherData
        {
            LastUpdated = DateTime.Now,
            OutsideTemperature = Math.Round(baseTemp, 1),
            InsideTemperature = Math.Round(baseTemp + ((decimal)_random.NextSingle() - 0.5m) * 5, 1),
            OutsideHumidity = humidity,
            InsideHumidity = (byte)(humidity + _random.Next(-10, 10)),
            WindSpeed = windSpeed,
            WindDirection = windDirection,
            Barometer = Math.Round(barometer, 2),
            RainRate = Math.Round(rainRate, 2),
            DailyRainAmount = Math.Round(dailyRain, 2),
            UvIndex = uvIndex,
            SolarRadiation = solarRadiation,
            SunriseTime = new TimeOnly(6, 30),
            SunsetTime = new TimeOnly(19, 45),
            WeatherCondition = GetWeatherCondition(rainRate, windSpeed, baseTemp),
            WindDirectionText = GetWindDirectionText(windDirection)
        };
    }
    
    private static string GetWeatherCondition(decimal rainRate, byte windSpeed, decimal temperature)
    {
        if (rainRate > 0.1m) return "Rainy";
        if (windSpeed > 10) return "Windy";
        if (temperature > 80) return "Hot";
        if (temperature < 60) return "Cool";
        return "Clear";
    }
    
    private static string GetWindDirectionText(short degrees)
    {
        var directions = new[] { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        var index = (int)Math.Round(degrees / 22.5) % 16;
        return directions[index];
    }
}
