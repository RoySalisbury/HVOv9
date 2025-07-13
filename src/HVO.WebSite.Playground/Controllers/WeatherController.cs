using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using HVO.DataModels.Data;
using HVO.DataModels.Models;
using HVO.DataModels.RawModels;
using Microsoft.EntityFrameworkCore;

namespace HVO.WebSite.Playground.Controllers
{
    /// <summary>
    /// Weather API controller for retrieving current weather conditions and daily highs/lows
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/weather")]
    public class WeatherController : ControllerBase
    {
        private readonly HvoDbContext _context;
        private readonly ILogger<WeatherController> _logger;

        public WeatherController(HvoDbContext context, ILogger<WeatherController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Gets the latest weather record
        /// </summary>
        /// <returns>The most recent weather data</returns>
        [HttpGet("latest")]
        public async Task<ActionResult<DavisVantageProConsoleRecordsNew>> GetLatestWeatherRecord()
        {
            try
            {
                _logger.LogInformation("Retrieving latest weather record");

                var latestRecord = await _context.DavisVantageProConsoleRecordsNews
                    .OrderByDescending(x => x.RecordDateTime)
                    .FirstOrDefaultAsync();

                if (latestRecord == null)
                {
                    _logger.LogWarning("No weather records found in database");
                    return NotFound(new { message = "No weather records found" });
                }

                _logger.LogInformation("Successfully retrieved latest weather record from {DateTime}", 
                    latestRecord.RecordDateTime);

                return Ok(new
                {
                    timestamp = DateTime.UtcNow,
                    machineName = Environment.MachineName,
                    data = latestRecord
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving latest weather record");
                return StatusCode(500, new { message = "Internal server error retrieving weather data" });
            }
        }

        /// <summary>
        /// Gets weather highs and lows for a specified date range
        /// </summary>
        /// <param name="startDate">Start date (defaults to today)</param>
        /// <param name="endDate">End date (defaults to tomorrow)</param>
        /// <returns>Weather highs and lows summary</returns>
        [HttpGet("highs-lows")]
        public async Task<ActionResult<WeatherRecordHighLowSummary>> GetWeatherHighsLows(
            [FromQuery] DateTimeOffset? startDate = null,
            [FromQuery] DateTimeOffset? endDate = null)
        {
            try
            {
                // Default to today if no dates provided
                var start = startDate ?? DateTimeOffset.Now.Date;
                var end = endDate ?? start.AddDays(1);

                _logger.LogInformation("Retrieving weather highs/lows from {StartDate} to {EndDate}", start, end);

                var summary = await _context.GetWeatherRecordHighLowSummary(start, end);

                if (summary == null)
                {
                    _logger.LogWarning("No weather summary found for date range {StartDate} to {EndDate}", start, end);
                    return NotFound(new { message = "No weather data found for the specified date range" });
                }

                _logger.LogInformation("Successfully retrieved weather highs/lows summary");

                return Ok(new
                {
                    timestamp = DateTime.UtcNow,
                    machineName = Environment.MachineName,
                    dateRange = new { start, end },
                    data = summary
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving weather highs/lows");
                return StatusCode(500, new { message = "Internal server error retrieving weather highs/lows" });
            }
        }

        /// <summary>
        /// Gets current weather conditions including today's highs and lows
        /// </summary>
        /// <returns>Current weather with today's extremes</returns>
        [HttpGet("current")]
        public async Task<ActionResult> GetCurrentWeatherConditions()
        {
            try
            {
                _logger.LogInformation("Retrieving current weather conditions with today's highs/lows");

                // Get latest weather record
                var latestRecord = await _context.DavisVantageProConsoleRecordsNews
                    .OrderByDescending(x => x.RecordDateTime)
                    .FirstOrDefaultAsync();

                if (latestRecord == null)
                {
                    _logger.LogWarning("No current weather records found");
                    return NotFound(new { message = "No current weather data available" });
                }

                // Get today's highs and lows
                var today = DateTimeOffset.Now.Date;
                var tomorrow = today.AddDays(1);
                var todaysHighsLows = await _context.GetWeatherRecordHighLowSummary(today, tomorrow);

                var response = new
                {
                    timestamp = DateTime.UtcNow,
                    machineName = Environment.MachineName,
                    current = new
                    {
                        recordDateTime = latestRecord.RecordDateTime,
                        outsideTemperature = latestRecord.OutsideTemperature,
                        outsideHumidity = latestRecord.OutsideHumidity,
                        insideTemperature = latestRecord.InsideTemperature,
                        insideHumidity = latestRecord.InsideHumidity,
                        windSpeed = latestRecord.WindSpeed,
                        windDirection = latestRecord.WindDirection,
                        barometer = latestRecord.Barometer,
                        barometerTrend = latestRecord.BarometerTrend,
                        rainRate = latestRecord.RainRate,
                        dailyRainAmount = latestRecord.DailyRainAmount,
                        monthlyRainAmount = latestRecord.MonthlyRainAmount,
                        yearlyRainAmount = latestRecord.YearlyRainAmount,
                        uvIndex = latestRecord.UvIndex,
                        solarRadiation = latestRecord.SolarRadiation,
                        outsideHeatIndex = latestRecord.OutsideHeatIndex,
                        outsideWindChill = latestRecord.OutsideWindChill,
                        outsideDewpoint = latestRecord.OutsideDewpoint,
                        sunriseTime = latestRecord.SunriseTime,
                        sunsetTime = latestRecord.SunsetTime
                    },
                    todaysExtremes = todaysHighsLows != null ? new
                    {
                        outsideTemperature = new
                        {
                            high = todaysHighsLows.OutsideTemperatureHigh,
                            highTime = todaysHighsLows.OutsideTemperatureHighDateTime,
                            low = todaysHighsLows.OutsideTemperatureLow,
                            lowTime = todaysHighsLows.OutsideTemperatureLowDateTime
                        },
                        insideTemperature = new
                        {
                            high = todaysHighsLows.InsideTemperatureHigh,
                            highTime = todaysHighsLows.InsideTemperatureHighDateTime,
                            low = todaysHighsLows.InsideTemperatureLow,
                            lowTime = todaysHighsLows.InsideTemperatureLowDateTime
                        },
                        outsideHumidity = new
                        {
                            high = todaysHighsLows.OutsideHumidityHigh,
                            highTime = todaysHighsLows.OutsideHumidityHighDateTime,
                            low = todaysHighsLows.OutsideHumidityLow,
                            lowTime = todaysHighsLows.OutsideHumidityLowDateTime
                        },
                        insideHumidity = new
                        {
                            high = todaysHighsLows.InsideHumidityHigh,
                            highTime = todaysHighsLows.InsideHumidityHighDateTime,
                            low = todaysHighsLows.InsideHumidityLow,
                            lowTime = todaysHighsLows.InsideHumidityLowDateTime
                        },
                        windSpeed = new
                        {
                            high = todaysHighsLows.WindSpeedHigh,
                            highTime = todaysHighsLows.WindSpeedHighDateTime,
                            highDirection = todaysHighsLows.WindSpeedHighDirection,
                            low = todaysHighsLows.WindSpeedLow,
                            lowTime = todaysHighsLows.WindSpeedLowDateTime,
                            lowDirection = todaysHighsLows.WindSpeedLowDirection
                        },
                        barometer = new
                        {
                            high = todaysHighsLows.BarometerHigh,
                            highTime = todaysHighsLows.BarometerHighDateTime,
                            low = todaysHighsLows.BarometerLow,
                            lowTime = todaysHighsLows.BarometerLowDateTime
                        },
                        heatIndex = new
                        {
                            high = todaysHighsLows.OutsideHeatIndexHigh,
                            highTime = todaysHighsLows.OutsideHeatIndexHighDateTime,
                            low = todaysHighsLows.OutsideHeatIndexLow,
                            lowTime = todaysHighsLows.OutsideHeatIndexLowDateTime
                        },
                        windChill = new
                        {
                            high = todaysHighsLows.OutsideWindChillHigh,
                            highTime = todaysHighsLows.OutsideWindChillHighDateTime,
                            low = todaysHighsLows.OutsideWindChillLow,
                            lowTime = todaysHighsLows.OutsideWindChillLowDateTime
                        },
                        dewPoint = new
                        {
                            high = todaysHighsLows.OutsideDewpointHigh,
                            highTime = todaysHighsLows.OutsideDewpointHighDateTime,
                            low = todaysHighsLows.OutsideDewpointLow,
                            lowTime = todaysHighsLows.OutsideDewpointLowDateTime
                        },
                        solarRadiation = new
                        {
                            high = todaysHighsLows.SolarRadiationHigh,
                            highTime = todaysHighsLows.SolarRadiationHighDateTime
                        },
                        uvIndex = new
                        {
                            high = todaysHighsLows.UVIndexHigh,
                            highTime = todaysHighsLows.UVIndexHighDateTime
                        }
                    } : null
                };

                _logger.LogInformation("Successfully retrieved current weather conditions with today's extremes");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving current weather conditions");
                return StatusCode(500, new { message = "Internal server error retrieving current weather conditions" });
            }
        }
    }
}
