# Weather API Documentation

## Overview
The Weather API provides access to current weather conditions and historical highs/lows from the Hualapai Valley Observatory weather station using a Davis Vantage Pro console.

## Base URL
```
/api/v1.0/weather
```

## Endpoints

### 1. Get Latest Weather Record
**GET** `/api/v1.0/weather/latest`

Returns the most recent weather reading from the Davis Vantage Pro console.

**Response Example:**
```json
{
  "timestamp": "2025-07-13T12:30:00.000Z",
  "machineName": "HVO-SERVER",
  "data": {
    "id": 12345,
    "recordDateTime": "2025-07-13T12:25:00.000Z",
    "barometer": 30.15,
    "barometerTrend": 0,
    "insideTemperature": 75.2,
    "insideHumidity": 45,
    "outsideTemperature": 85.7,
    "outsideHumidity": 35,
    "windSpeed": 8,
    "windDirection": 225,
    "tenMinuteWindSpeedAverage": 6,
    "rainRate": 0.0,
    "uvIndex": 7,
    "solarRadiation": 650,
    "dailyRainAmount": 0.0,
    "monthlyRainAmount": 2.5,
    "yearlyRainAmount": 15.2,
    "outsideHeatIndex": 88.3,
    "outsideWindChill": 85.7,
    "outsideDewpoint": 58.2,
    "sunriseTime": "06:15:00",
    "sunsetTime": "19:45:00"
  }
}
```

### 2. Get Weather Highs and Lows
**GET** `/api/v1.0/weather/highs-lows`

Returns weather extremes for a specified date range using stored procedures.

**Query Parameters:**
- `startDate` (optional): Start date in ISO format (defaults to today)
- `endDate` (optional): End date in ISO format (defaults to tomorrow)

**Example Request:**
```
GET /api/v1.0/weather/highs-lows?startDate=2025-07-13T00:00:00Z&endDate=2025-07-14T00:00:00Z
```

**Response Example:**
```json
{
  "timestamp": "2025-07-13T12:30:00.000Z",
  "machineName": "HVO-SERVER",
  "dateRange": {
    "start": "2025-07-13T00:00:00.000Z",
    "end": "2025-07-14T00:00:00.000Z"
  },
  "data": {
    "startRecordDateTime": "2025-07-13T00:00:00.000Z",
    "endRecordDateTime": "2025-07-14T00:00:00.000Z",
    "outsideTemperatureHigh": 92.5,
    "outsideTemperatureLow": 68.2,
    "outsideTemperatureHighDateTime": "2025-07-13T15:30:00.000Z",
    "outsideTemperatureLowDateTime": "2025-07-13T06:15:00.000Z",
    "insideTemperatureHigh": 78.1,
    "insideTemperatureLow": 72.4,
    "windSpeedHigh": 15,
    "windSpeedLow": 0,
    "windSpeedHighDirection": 270,
    "windSpeedLowDirection": 0,
    "barometerHigh": 30.25,
    "barometerLow": 29.95,
    "solarRadiationHigh": 850,
    "uvIndexHigh": 9
  }
}
```

### 3. Get Current Weather Conditions
**GET** `/api/v1.0/weather/current`

Returns current weather conditions combined with today's highs and lows in a single response.

**Response Example:**
```json
{
  "timestamp": "2025-07-13T12:30:00.000Z",
  "machineName": "HVO-SERVER",
  "current": {
    "recordDateTime": "2025-07-13T12:25:00.000Z",
    "outsideTemperature": 85.7,
    "outsideHumidity": 35,
    "insideTemperature": 75.2,
    "insideHumidity": 45,
    "windSpeed": 8,
    "windDirection": 225,
    "barometer": 30.15,
    "barometerTrend": 0,
    "rainRate": 0.0,
    "dailyRainAmount": 0.0,
    "monthlyRainAmount": 2.5,
    "yearlyRainAmount": 15.2,
    "uvIndex": 7,
    "solarRadiation": 650,
    "outsideHeatIndex": 88.3,
    "outsideWindChill": 85.7,
    "outsideDewpoint": 58.2,
    "sunriseTime": "06:15:00",
    "sunsetTime": "19:45:00"
  },
  "todaysExtremes": {
    "outsideTemperature": {
      "high": 92.5,
      "highTime": "2025-07-13T15:30:00.000Z",
      "low": 68.2,
      "lowTime": "2025-07-13T06:15:00.000Z"
    },
    "insideTemperature": {
      "high": 78.1,
      "highTime": "2025-07-13T14:20:00.000Z",
      "low": 72.4,
      "lowTime": "2025-07-13T07:30:00.000Z"
    },
    "windSpeed": {
      "high": 15,
      "highTime": "2025-07-13T13:45:00.000Z",
      "highDirection": 270,
      "low": 0,
      "lowTime": "2025-07-13T05:00:00.000Z",
      "lowDirection": 0
    },
    "barometer": {
      "high": 30.25,
      "highTime": "2025-07-13T08:00:00.000Z",
      "low": 29.95,
      "lowTime": "2025-07-13T16:00:00.000Z"
    }
  }
}
```

## Data Sources

### Database Tables
- **DavisVantageProConsoleRecordsNew**: Current weather readings
- **sp__GetWeatherRecordHighLowSummary**: Stored procedure for highs/lows

### Weather Station
- **Davis Vantage Pro Weather Console**: Professional-grade weather monitoring equipment
- **Location**: Hualapai Valley Observatory, Arizona
- **Update Frequency**: Real-time data collection

## Error Responses

### 404 Not Found
```json
{
  "message": "No weather records found"
}
```

### 500 Internal Server Error
```json
{
  "message": "Internal server error retrieving weather data"
}
```

## Configuration

### Connection String
The API requires a connection string named `HualapaiValleyObservatory` in the application configuration.

### Dependencies
- **HVO.DataModels**: Entity Framework models and DbContext
- **Entity Framework Core**: Database access with SQL Server provider
- **ASP.NET Core**: Web API framework with versioning

## Usage Examples

### JavaScript/Fetch API
```javascript
// Get current weather with today's extremes
fetch('/api/v1.0/weather/current')
  .then(response => response.json())
  .then(data => {
    console.log('Current Temperature:', data.current.outsideTemperature);
    console.log('Today\'s High:', data.todaysExtremes.outsideTemperature.high);
    console.log('Today\'s Low:', data.todaysExtremes.outsideTemperature.low);
  });

// Get yesterday's highs and lows
const yesterday = new Date();
yesterday.setDate(yesterday.getDate() - 1);
const today = new Date();

const params = new URLSearchParams({
  startDate: yesterday.toISOString(),
  endDate: today.toISOString()
});

fetch(`/api/v1.0/weather/highs-lows?${params}`)
  .then(response => response.json())
  .then(data => console.log('Yesterday\'s Extremes:', data.data));
```

### cURL
```bash
# Get latest weather reading
curl -X GET "http://localhost:5136/api/v1.0/weather/latest"

# Get current conditions with today's extremes
curl -X GET "http://localhost:5136/api/v1.0/weather/current"

# Get highs/lows for a specific date
curl -X GET "http://localhost:5136/api/v1.0/weather/highs-lows?startDate=2025-07-13T00:00:00Z&endDate=2025-07-14T00:00:00Z"
```

## Notes

- All timestamps are in UTC format
- Temperature values are in Fahrenheit
- Wind speeds are in miles per hour (mph)
- Barometric pressure is in inches of mercury (inHg)
- Rain amounts are in inches
- Wind directions are in degrees (0-359)
- UV Index is unitless (0-11+ scale)
- Solar radiation is in watts per square meter (w/m²)
