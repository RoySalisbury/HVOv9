namespace HVO.WebSite.RoofControllerV4.Models;

/// <summary>
/// Weather data model for displaying current conditions
/// </summary>
public class WeatherData
{
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public decimal OutsideTemperature { get; set; }
    public decimal InsideTemperature { get; set; }
    public byte OutsideHumidity { get; set; }
    public byte InsideHumidity { get; set; }
    public byte WindSpeed { get; set; }
    public short WindDirection { get; set; }
    public decimal Barometer { get; set; }
    public decimal RainRate { get; set; }
    public decimal DailyRainAmount { get; set; }
    public byte UvIndex { get; set; }
    public short SolarRadiation { get; set; }
    public TimeOnly? SunriseTime { get; set; }
    public TimeOnly? SunsetTime { get; set; }
    public string WeatherCondition { get; set; } = "Clear";
    public string WindDirectionText { get; set; } = "N";
}
