# NINA API Guider Equipment Methods Documentation

This document describes the guider equipment methods available in the NINA API client for HVOv9. These methods provide full control over guiding equipment including autoguiders and guide cameras.

## Overview

The guider equipment methods follow the established NINA API patterns and provide access to:
- Guider device information and status
- Device connection management
- Guiding operations (start/stop)
- Calibration management
- Guide performance monitoring

All methods return `Result<T>` for consistent error handling and follow the HVOv9 structured logging patterns.

## API Specification Compliance

These methods implement the official NINA API v2.2.6 specification:
- **Base URL**: `/v2/api/equipment/guider`
- **Specification**: https://github.com/christian-photo/ninaAPI/blob/main/ninaAPI/api_spec.yaml#L1400
- **Response Format**: Standard NINA API response envelope with `Success`, `Error`, `StatusCode`, and `Type` fields

## Available Methods

### 1. GetGuiderInfoAsync()

Gets detailed information about the currently connected guider device.

```csharp
Task<Result<GuiderInfoResponse>> GetGuiderInfoAsync(CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/info`

**Returns**: 
- `GuiderInfoResponse` containing `GuiderInfo` object with:
  - Basic device information (Name, DisplayName, Description, DeviceId, etc.)
  - Guider capabilities (CanClearCalibration, CanSetShiftRate, CanGetLockPosition)
  - Current guiding state and performance metrics
  - RMS error information and pixel scale
  - Last guide step data

**Example Usage**:
```csharp
var result = await ninaClient.GetGuiderInfoAsync();
if (result.IsSuccess)
{
    var guiderInfo = result.Value.Response;
    Console.WriteLine($"Guider: {guiderInfo.DisplayName}");
    Console.WriteLine($"State: {guiderInfo.State}");
    Console.WriteLine($"RMS Error: {guiderInfo.RMSError?.Total}");
}
```

### 2. GetGuiderDevicesAsync()

Lists all available guider devices that can be connected to.

```csharp
Task<Result<IReadOnlyList<DeviceInfo>>> GetGuiderDevicesAsync(CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/list-devices`

**Returns**: 
- `IReadOnlyList<DeviceInfo>` containing list of available guider devices

**Example Usage**:
```csharp
var result = await ninaClient.GetGuiderDevicesAsync();
if (result.IsSuccess)
{
    foreach (var device in result.Value)
    {
        Console.WriteLine($"Available Guider: {device.DisplayName} - {device.DeviceId}");
    }
}
```

### 3. RescanGuiderDevicesAsync()

Rescans the system for available guider devices, useful when devices are connected/disconnected.

```csharp
Task<Result<IReadOnlyList<DeviceInfo>>> RescanGuiderDevicesAsync(CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/rescan`

**Returns**: 
- `IReadOnlyList<DeviceInfo>` containing updated list of available guider devices

### 4. ConnectGuiderAsync(string? to = null)

Connects to a guider device, optionally specifying which device to connect to.

```csharp
Task<Result<string>> ConnectGuiderAsync(string? to = null, CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/connect[?to=deviceId]`

**Parameters**:
- `to` (optional): Device identifier of the specific guider to connect to. If omitted, connects to the default device.

**Returns**: 
- String status message (typically "Connected" or "Already Connected")

**Example Usage**:
```csharp
// Connect to default guider
var result = await ninaClient.ConnectGuiderAsync();

// Connect to specific guider
var result = await ninaClient.ConnectGuiderAsync("PHD2 Guider");
```

### 5. DisconnectGuiderAsync()

Disconnects from the currently connected guider device.

```csharp
Task<Result<string>> DisconnectGuiderAsync(CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/disconnect`

**Returns**: 
- String status message (typically "Disconnected")

### 6. StartGuidingAsync(bool? calibrate = null)

Starts the guiding process, optionally with calibration.

```csharp
Task<Result<string>> StartGuidingAsync(bool? calibrate = null, CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/start[?calibrate=true/false]`

**Parameters**:
- `calibrate` (optional): Whether to perform calibration before starting guiding
  - `true`: Force calibration before guiding
  - `false`: Skip calibration and use existing calibration
  - `null`: Use guider's default behavior

**Returns**: 
- String status message indicating guiding start status

**Example Usage**:
```csharp
// Start guiding with default behavior
var result = await ninaClient.StartGuidingAsync();

// Start guiding with forced calibration
var result = await ninaClient.StartGuidingAsync(calibrate: true);

// Start guiding without calibration
var result = await ninaClient.StartGuidingAsync(calibrate: false);
```

### 7. StopGuidingAsync()

Stops the current guiding process.

```csharp
Task<Result<string>> StopGuidingAsync(CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/stop`

**Returns**: 
- String status message indicating guiding stop status

### 8. ClearGuiderCalibrationAsync()

Clears the current guider calibration data, forcing recalibration on next guiding session.

```csharp
Task<Result<string>> ClearGuiderCalibrationAsync(CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/clear-calibration`

**Returns**: 
- String status message indicating calibration clear status

**Note**: This method is only available if the guider supports calibration clearing (check `GuiderInfo.CanClearCalibration`).

### 9. GetGuiderGraphAsync()

Retrieves the guiding performance graph data (guide steps history).

```csharp
Task<Result<GuideStepsHistory>> GetGuiderGraphAsync(CancellationToken cancellationToken = default)
```

**Endpoint**: `GET /v2/api/equipment/guider/graph`

**Returns**: 
- `GuideStepsHistory` containing guide performance data with:
  - `RMS`: RMS error statistics (RA, Dec, Total, Peak values)
  - `GuideSteps`: Array of individual guide steps with detailed information
  - `Interval`: Guide interval setting
  - `MaxY`/`MinY`: Y-axis range for distance measurements
  - `MaxDurationY`/`MinDurationY`: Y-axis range for duration measurements
  - `HistorySize`: Number of guide steps stored in history
  - `PixelScale`: Guide camera pixel scale in arcseconds/pixel
  - `Scale`: Display scale factor

**Example Usage**:
```csharp
var result = await ninaClient.GetGuiderGraphAsync();
if (result.IsSuccess)
{
    var guideHistory = result.Value;
    Console.WriteLine($"RMS Total Error: {guideHistory.RMS.Total:F2} pixels");
    Console.WriteLine($"Guide Steps Count: {guideHistory.GuideSteps.Count}");
    
    foreach (var step in guideHistory.GuideSteps.Take(5))
    {
        Console.WriteLine($"Step {step.Id}: RA={step.RADistanceRaw:F2}px, DEC={step.DECDistanceRaw:F2}px");
    }
}
```

## Data Models

### GuiderInfo
Extends `DeviceInfo` with guider-specific properties:
- `CanClearCalibration`: Whether the guider supports clearing calibration
- `CanSetShiftRate`: Whether the guider supports setting shift rates
- `CanGetLockPosition`: Whether the guider can report lock position
- `RMSError`: Current RMS error statistics
- `PixelScale`: Guide camera pixel scale in arcseconds/pixel
- `LastGuideStep`: Most recent guide correction
- `State`: Current guiding state (e.g., "Stopped", "Guiding", "Calibrating")

### GuideStepsHistory
Complete guiding performance history:
- `RMS`: RMS error statistics object
- `GuideSteps`: Array of `GuideStepHistory` objects with extended information
- `Interval`: Guide interval in milliseconds
- `MaxY`/`MinY`: Y-axis range for distance display
- `MaxDurationY`/`MinDurationY`: Y-axis range for duration display
- `HistorySize`: Maximum number of steps stored
- `PixelScale`: Pixel scale in arcseconds/pixel
- `Scale`: Display scale factor

### GuideRMS
RMS error statistics (referenced in GuideStepsHistory):
- `RA`/`Dec`/`Total`: Numeric RMS values in pixels
- `RAText`/`DecText`/`TotalText`: Formatted text representations
- `PeakRA`/`PeakDec`: Peak error values
- `PeakRAText`/`PeakDecText`: Formatted peak values
- `DataPoints`: Number of data points used for calculation
- `Scale`: Scale factor for display

### GuideStepHistory
Individual guide correction data with extended display information:
- `Id`: Step sequence number
- `IdOffsetLeft`/`IdOffsetRight`: Display offset positioning
- `RADistanceRaw`/`DECDistanceRaw`: Raw error in pixels
- `RADistanceRawDisplay`/`DECDistanceRawDisplay`: Display-formatted error values
- `RADuration`/`DECDuration`: Correction pulse duration in milliseconds
- `Dither`: Dithering information

## Error Handling

All methods follow the HVOv9 `Result<T>` pattern:

```csharp
var result = await ninaClient.StartGuidingAsync();
if (!result.IsSuccess)
{
    logger.LogError("Failed to start guiding: {Error}", result.Exception?.Message);
    // Handle error appropriately
}
```

Common error scenarios:
- **Device not connected**: Connect to guider first using `ConnectGuiderAsync()`
- **Already guiding**: Check guider state before starting
- **Calibration required**: Use `StartGuidingAsync(calibrate: true)` or calibrate separately
- **Hardware communication errors**: Check device connection and NINA status

## Logging

All methods implement structured logging following HVOv9 standards:
- **Information level**: Connection operations, guiding start/stop, calibration actions
- **Debug level**: Status queries and device information requests  
- **Trace level**: Low-level API request details

Example log entries:
```
[INFO] Connecting to guider device - DeviceId: PHD2 Guider
[INFO] Starting guiding - Calibrate: true
[DEBUG] Getting guider equipment information
```

## Integration Examples

### Complete Guiding Session
```csharp
// 1. Check available devices
var devicesResult = await ninaClient.GetGuiderDevicesAsync();
if (!devicesResult.IsSuccess) return;

// 2. Connect to specific guider
var connectResult = await ninaClient.ConnectGuiderAsync("PHD2 Guider");
if (!connectResult.IsSuccess) return;

// 3. Get guider information
var infoResult = await ninaClient.GetGuiderInfoAsync();
if (infoResult.IsSuccess)
{
    var info = infoResult.Value.Response;
    Console.WriteLine($"Connected to: {info.DisplayName}");
    Console.WriteLine($"State: {info.State}");
}

// 4. Start guiding with calibration
var startResult = await ninaClient.StartGuidingAsync(calibrate: true);
if (startResult.IsSuccess)
{
    Console.WriteLine("Guiding started successfully");
}

// 5. Monitor guiding performance
var graphResult = await ninaClient.GetGuiderGraphAsync();
if (graphResult.IsSuccess)
{
    var history = graphResult.Value;
    Console.WriteLine($"Guide history contains {history.GuideSteps.Count} steps");
    Console.WriteLine($"RMS Total Error: {history.RMS.Total:F2} pixels");
    Console.WriteLine($"Peak RA Error: {history.RMS.PeakRA:F2} pixels");
}

// 6. Stop guiding when done
var stopResult = await ninaClient.StopGuidingAsync();
if (stopResult.IsSuccess)
{
    Console.WriteLine("Guiding stopped");
}
```

### Guiding Status Monitoring
```csharp
public async Task<GuiderStatus> GetGuidingStatusAsync()
{
    var result = await ninaClient.GetGuiderInfoAsync();
    if (!result.IsSuccess)
    {
        return new GuiderStatus { IsConnected = false, Error = result.Exception?.Message };
    }

    var info = result.Value.Response;
    return new GuiderStatus
    {
        IsConnected = info.Connected,
        State = info.State,
        RmsError = info.RMSError?.Total ?? 0,
        PixelScale = info.PixelScale,
        LastGuideStep = info.LastGuideStep
    };
}
```

This completes the NINA API guider equipment method implementation following the established HVOv9 patterns and official API specification.
