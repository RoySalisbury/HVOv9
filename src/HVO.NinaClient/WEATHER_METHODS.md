# NINA API Weather Equipment Methods

This document describes the weather equipment methods available in the NINA API client for monitoring weather conditions from connected weather devices.

## Overview

Weather devices in NINA provide real-time monitoring of environmental conditions critical for astronomical observations. These devices can monitor temperature, humidity, wind speed, cloud cover, and other weather parameters to help ensure safe and optimal observing conditions.

## Available Methods

### GetWeatherInfoAsync()
Get detailed information about the currently connected weather device, including all weather readings and device status.

```csharp
var result = await ninaClient.GetWeatherInfoAsync();
if (result.IsSuccessful)
{
    var weatherInfo = result.Value.Response;
    Console.WriteLine($"Weather Device: {weatherInfo.Name}");
    Console.WriteLine($"Connected: {weatherInfo.Connected}");
    Console.WriteLine($"Temperature: {weatherInfo.Temperature}°C");
    Console.WriteLine($"Humidity: {weatherInfo.Humidity}%");
    Console.WriteLine($"Pressure: {weatherInfo.Pressure} hPa");
    Console.WriteLine($"Wind Speed: {weatherInfo.WindSpeed} m/s");
    Console.WriteLine($"Wind Direction: {weatherInfo.WindDirection}°");
    Console.WriteLine($"Cloud Cover: {weatherInfo.CloudCover}%");
    Console.WriteLine($"Dew Point: {weatherInfo.DewPoint}°C");
    
    // Optional readings (may be null for some devices)
    if (!string.IsNullOrEmpty(weatherInfo.RainRate))
        Console.WriteLine($"Rain Rate: {weatherInfo.RainRate}");
    if (!string.IsNullOrEmpty(weatherInfo.SkyBrightness))
        Console.WriteLine($"Sky Brightness: {weatherInfo.SkyBrightness}");
    if (!string.IsNullOrEmpty(weatherInfo.SkyQuality))
        Console.WriteLine($"Sky Quality: {weatherInfo.SkyQuality}");
    if (!string.IsNullOrEmpty(weatherInfo.SkyTemperature))
        Console.WriteLine($"Sky Temperature: {weatherInfo.SkyTemperature}");
    if (!string.IsNullOrEmpty(weatherInfo.StarFWHM))
        Console.WriteLine($"Star FWHM: {weatherInfo.StarFWHM}");
    if (!string.IsNullOrEmpty(weatherInfo.WindGust))
        Console.WriteLine($"Wind Gust: {weatherInfo.WindGust}");
}
```

### ConnectWeatherAsync(deviceId?)
Connect to a weather device. If no deviceId is provided, connects to the default/selected device.

```csharp
// Connect to default weather device
var result = await ninaClient.ConnectWeatherAsync();

// Connect to specific weather device
var result = await ninaClient.ConnectWeatherAsync("ASCOM.Boltwood.OkToImage.SafetyMonitor");

if (result.IsSuccessful)
{
    Console.WriteLine($"Weather connection status: {result.Value}");
}
```

### DisconnectWeatherAsync()
Disconnect from the currently connected weather device.

```csharp
var result = await ninaClient.DisconnectWeatherAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Weather disconnection status: {result.Value}");
}
```

### GetWeatherDevicesAsync()
Get a list of all available weather devices that can be connected to.

```csharp
var result = await ninaClient.GetWeatherDevicesAsync();
if (result.IsSuccessful)
{
    Console.WriteLine("Available Weather Devices:");
    foreach (var device in result.Value)
    {
        Console.WriteLine($"  {device.Name} - {device.Id}");
        if (!string.IsNullOrEmpty(device.Description))
            Console.WriteLine($"    Description: {device.Description}");
    }
}
```

### RescanWeatherDevicesAsync()
Rescan for new weather devices and get the updated list of available devices.

```csharp
var result = await ninaClient.RescanWeatherDevicesAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Found {result.Value.Count} weather devices after rescan:");
    foreach (var device in result.Value)
    {
        Console.WriteLine($"  {device.Name} - {device.Id}");
    }
}
```

## Weather Data Properties

The `WeatherInfo` class contains the following weather measurements:

### Core Weather Properties
- **Temperature** (double): Ambient temperature in degrees Celsius
- **Humidity** (int): Relative humidity percentage (0-100)
- **Pressure** (int): Atmospheric pressure in hPa
- **WindSpeed** (double): Wind speed in m/s
- **WindDirection** (int): Wind direction in degrees (0-360)
- **CloudCover** (int): Cloud coverage percentage (0-100)
- **DewPoint** (double): Dew point temperature in degrees Celsius

### Optional Properties
Some weather devices may provide additional measurements:
- **RainRate** (string): Rain rate measurement
- **SkyBrightness** (string): Sky brightness reading
- **SkyQuality** (string): Sky quality assessment
- **SkyTemperature** (string): Sky temperature measurement
- **StarFWHM** (string): Star Full Width at Half Maximum measurement
- **WindGust** (string): Wind gust information

### Device Properties
- **AveragePeriod** (int): Averaging period for measurements
- **Connected** (bool): Weather device connection status
- **Name** (string): Weather device name
- **Description** (string): Weather device description

## API Endpoints

The weather methods correspond to these NINA API v2.2.6 endpoints:

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GetWeatherInfoAsync()` | `GET /v2/api/equipment/weather/info` | Get weather device information and readings |
| `ConnectWeatherAsync()` | `GET /v2/api/equipment/weather/connect` | Connect to weather device |
| `DisconnectWeatherAsync()` | `GET /v2/api/equipment/weather/disconnect` | Disconnect weather device |
| `GetWeatherDevicesAsync()` | `GET /v2/api/equipment/weather/list-devices` | List available weather sources |
| `RescanWeatherDevicesAsync()` | `GET /v2/api/equipment/weather/rescan` | Rescan for weather sources |

## Error Handling

All methods return `Result<T>` objects that should be checked for success:

```csharp
var result = await ninaClient.GetWeatherInfoAsync();
if (!result.IsSuccessful)
{
    Console.WriteLine($"Weather operation failed: {result.Error?.Message}");
    // Handle the error appropriately
}
```

## Common Weather Device Types

Popular weather devices that work with NINA include:
- **Boltwood Cloud Sensor**: Cloud, sky temperature, wind, rain detection
- **AAG CloudWatcher**: Cloud cover, sky temperature, brightness, rain
- **Davis Weather Stations**: Comprehensive weather monitoring
- **OpenWeatherMap**: Internet-based weather data
- **ASCOM Weather Simulators**: For testing and development

## Integration with Safety Systems

Weather data is often used in conjunction with safety monitors to automatically:
- Close dome shutters during poor weather
- Park telescopes when wind speeds are too high
- Pause imaging sequences during cloudy conditions
- Alert operators to changing conditions

The weather information can be monitored continuously and integrated with NINA's safety systems for automated observatory operations.
