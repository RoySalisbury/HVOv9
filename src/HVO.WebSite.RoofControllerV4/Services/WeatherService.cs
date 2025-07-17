using HVO.WebSite.RoofControllerV4.Models;
using System.Text.Json;

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
/// Weather service implementation that connects to the HVO Playground weather API
/// </summary>
public class WeatherService : IWeatherService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WeatherService> _logger;
    private readonly string _apiBaseUrl = "https://hvo-playground.niceforest-7078fd44.westus3.azurecontainerapps.io";

    public WeatherService(HttpClient httpClient, ILogger<WeatherService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WeatherData> GetCurrentWeatherAsync()
    {
        try
        {
            _logger.LogInformation("Fetching current weather data from HVO Playground API");
            
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/api/v1.0/weather/current");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Weather API returned status code: {StatusCode}", response.StatusCode);
                return GetFallbackWeatherData();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<CurrentWeatherApiResponse>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse?.Current == null)
            {
                _logger.LogWarning("Weather API returned null or invalid data");
                return GetFallbackWeatherData();
            }

            var weatherData = MapApiResponseToWeatherData(apiResponse);
            _logger.LogInformation("Successfully retrieved weather data from API");
            return weatherData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching weather data from API, using fallback data");
            return GetFallbackWeatherData();
        }
    }

    private WeatherData MapApiResponseToWeatherData(CurrentWeatherApiResponse apiResponse)
    {
        var current = apiResponse.Current;
        var extremes = apiResponse.TodaysExtremes;

        var weatherData = new WeatherData
        {
            LastUpdated = current.RecordDateTime?.DateTime ?? DateTime.Now,
            OutsideTemperature = current.OutsideTemperature ?? 0,
            InsideTemperature = current.InsideTemperature ?? 0,
            OutsideHumidity = (byte)(current.OutsideHumidity ?? 0),
            InsideHumidity = (byte)(current.InsideHumidity ?? 0),
            WindSpeed = (byte)(current.WindSpeed ?? 0),
            WindDirection = (short)(current.WindDirection ?? 0),
            Barometer = current.Barometer ?? 0,
            RainRate = current.RainRate ?? 0,
            DailyRainAmount = current.DailyRainAmount ?? 0,
            UvIndex = (byte)(current.UvIndex ?? 0),
            SolarRadiation = (short)(current.SolarRadiation ?? 0),
            SunriseTime = current.SunriseTime,
            SunsetTime = current.SunsetTime,
            WeatherCondition = GetWeatherCondition(current),
            WindDirectionText = GetWindDirectionText(current.WindDirection ?? 0)
        };

        // Add today's extremes if available
        if (extremes != null)
        {
            weatherData.TodaysHighTemp = extremes.OutsideTemperature?.High;
            weatherData.TodaysLowTemp = extremes.OutsideTemperature?.Low;
            weatherData.TodaysHighTempTime = extremes.OutsideTemperature?.HighTime?.TimeOfDay is TimeSpan high ? TimeOnly.FromTimeSpan(high) : null;
            weatherData.TodaysLowTempTime = extremes.OutsideTemperature?.LowTime?.TimeOfDay is TimeSpan low ? TimeOnly.FromTimeSpan(low) : null;
            weatherData.TodaysHighHumidity = (byte?)(extremes.OutsideHumidity?.High ?? 0);
            weatherData.TodaysLowHumidity = (byte?)(extremes.OutsideHumidity?.Low ?? 0);
            weatherData.TodaysHighWindSpeed = (byte?)(extremes.WindSpeed?.High ?? 0);
            weatherData.TodaysHighBarometer = extremes.Barometer?.High;
            weatherData.TodaysLowBarometer = extremes.Barometer?.Low;
        }

        return weatherData;
    }

    private string GetWeatherCondition(CurrentWeatherApiData current)
    {
        // Simple logic to determine weather condition based on available data
        if (current.RainRate > 0)
            return "Rainy";
        
        if (current.SolarRadiation > 500)
            return "Sunny";
        
        if (current.SolarRadiation > 200)
            return "Partly Cloudy";
        
        return "Cloudy";
    }

    private string GetWindDirectionText(decimal windDirection)
    {
        var directions = new[] { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        var index = (int)Math.Round(windDirection / 22.5m) % 16;
        return directions[index];
    }

    private WeatherData GetFallbackWeatherData()
    {
        // Return fallback data when API is unavailable
        _logger.LogInformation("Using fallback weather data");
        
        return new WeatherData
        {
            LastUpdated = DateTime.Now,
            OutsideTemperature = 75.0m,
            InsideTemperature = 72.0m,
            OutsideHumidity = 45,
            InsideHumidity = 40,
            WindSpeed = 5,
            WindDirection = 180,
            Barometer = 29.92m,
            RainRate = 0,
            DailyRainAmount = 0,
            UvIndex = 3,
            SolarRadiation = 400,
            SunriseTime = new TimeOnly(6, 30),
            SunsetTime = new TimeOnly(19, 45),
            WeatherCondition = "API Unavailable",
            WindDirectionText = "S"
        };
    }
}

/// <summary>
/// API response model for current weather conditions
/// </summary>
public class CurrentWeatherApiResponse
{
    public DateTime Timestamp { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public CurrentWeatherApiData Current { get; set; } = new();
    public TodaysExtremesApiData? TodaysExtremes { get; set; }
}

/// <summary>
/// Current weather data from the API
/// </summary>
public class CurrentWeatherApiData
{
    public DateTimeOffset? RecordDateTime { get; set; }
    public decimal? OutsideTemperature { get; set; }
    public decimal? OutsideHumidity { get; set; }
    public decimal? InsideTemperature { get; set; }
    public decimal? InsideHumidity { get; set; }
    public decimal? WindSpeed { get; set; }
    public decimal? WindDirection { get; set; }
    public decimal? Barometer { get; set; }
    public short? BarometerTrend { get; set; }
    public decimal? RainRate { get; set; }
    public decimal? DailyRainAmount { get; set; }
    public decimal? MonthlyRainAmount { get; set; }
    public decimal? YearlyRainAmount { get; set; }
    public decimal? UvIndex { get; set; }
    public decimal? SolarRadiation { get; set; }
    public decimal? OutsideHeatIndex { get; set; }
    public decimal? OutsideWindChill { get; set; }
    public decimal? OutsideDewpoint { get; set; }
    public TimeOnly? SunriseTime { get; set; }
    public TimeOnly? SunsetTime { get; set; }
}

/// <summary>
/// Today's weather extremes from the API
/// </summary>
public class TodaysExtremesApiData
{
    public TemperatureExtremesApi OutsideTemperature { get; set; } = new();
    public TemperatureExtremesApi InsideTemperature { get; set; } = new();
    public HumidityExtremesApi OutsideHumidity { get; set; } = new();
    public HumidityExtremesApi InsideHumidity { get; set; } = new();
    public WindSpeedExtremesApi WindSpeed { get; set; } = new();
    public BarometerExtremesApi Barometer { get; set; } = new();
    public TemperatureExtremesApi HeatIndex { get; set; } = new();
    public TemperatureExtremesApi WindChill { get; set; } = new();
    public TemperatureExtremesApi DewPoint { get; set; } = new();
    public SolarRadiationExtremesApi SolarRadiation { get; set; } = new();
    public UVIndexExtremesApi UvIndex { get; set; } = new();
}

/// <summary>
/// Temperature extremes from the API
/// </summary>
public class TemperatureExtremesApi
{
    public decimal? High { get; set; }
    public DateTimeOffset? HighTime { get; set; }
    public decimal? Low { get; set; }
    public DateTimeOffset? LowTime { get; set; }
}

/// <summary>
/// Humidity extremes from the API
/// </summary>
public class HumidityExtremesApi
{
    public decimal? High { get; set; }
    public DateTimeOffset? HighTime { get; set; }
    public decimal? Low { get; set; }
    public DateTimeOffset? LowTime { get; set; }
}

/// <summary>
/// Wind speed extremes from the API
/// </summary>
public class WindSpeedExtremesApi
{
    public decimal? High { get; set; }
    public DateTimeOffset? HighTime { get; set; }
    public decimal? HighDirection { get; set; }
    public decimal? Low { get; set; }
    public DateTimeOffset? LowTime { get; set; }
    public decimal? LowDirection { get; set; }
}

/// <summary>
/// Barometer extremes from the API
/// </summary>
public class BarometerExtremesApi
{
    public decimal? High { get; set; }
    public DateTimeOffset? HighTime { get; set; }
    public decimal? Low { get; set; }
    public DateTimeOffset? LowTime { get; set; }
}

/// <summary>
/// Solar radiation extremes from the API
/// </summary>
public class SolarRadiationExtremesApi
{
    public decimal? High { get; set; }
    public DateTimeOffset? HighTime { get; set; }
}

/// <summary>
/// UV index extremes from the API
/// </summary>
public class UVIndexExtremesApi
{
    public decimal? High { get; set; }
    public DateTimeOffset? HighTime { get; set; }
}
