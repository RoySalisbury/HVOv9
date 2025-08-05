# NINA Focuser Equipment Methods

This document describes the focuser equipment methods available in the NINA API client, which allow control and management of focuser devices for astrophotography applications.

## Overview

The focuser equipment methods provide comprehensive control over focuser hardware, including connection management, position control, and autofocus operations. These methods are compliant with the NINA API v2.2.6 specification.

## Available Methods

### Device Management

#### `GetFocuserInfoAsync()`
Gets detailed information about the currently connected focuser device.

**Returns:** `Result<FocuserInfo>`

**Example:**
```csharp
var result = await ninaClient.GetFocuserInfoAsync();
if (result.IsSuccessful)
{
    var focuser = result.Value;
    Console.WriteLine($"Position: {focuser.Position}");
    Console.WriteLine($"Temperature: {focuser.Temperature}°C");
    Console.WriteLine($"Is Moving: {focuser.IsMoving}");
}
```

#### `GetFocuserDevicesAsync()`
Lists all available focuser devices that can be connected.

**Returns:** `Result<IReadOnlyList<DeviceInfo>>`

**Example:**
```csharp
var result = await ninaClient.GetFocuserDevicesAsync();
if (result.IsSuccessful)
{
    foreach (var device in result.Value)
    {
        Console.WriteLine($"Device: {device.Name} (ID: {device.Id})");
    }
}
```

#### `RescanFocuserDevicesAsync()`
Rescans for new focuser devices and returns an updated list of available devices.

**Returns:** `Result<IReadOnlyList<DeviceInfo>>`

**Example:**
```csharp
var result = await ninaClient.RescanFocuserDevicesAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Found {result.Value.Count} focuser devices after rescan");
}
```

### Connection Management

#### `ConnectFocuserAsync(string? deviceId = null)`
Connects to a focuser device. If no device ID is specified, connects to the default device.

**Parameters:**
- `deviceId` (optional): The ID of the focuser device to connect to

**Returns:** `Result<string>`

**Example:**
```csharp
// Connect to default focuser
var result = await ninaClient.ConnectFocuserAsync();

// Connect to specific focuser by ID
var result2 = await ninaClient.ConnectFocuserAsync("ASCOM.Simulator.Focuser");

if (result.IsSuccessful)
{
    Console.WriteLine($"Focuser connection: {result.Value}");
}
```

#### `DisconnectFocuserAsync()`
Disconnects the currently connected focuser.

**Returns:** `Result<string>`

**Example:**
```csharp
var result = await ninaClient.DisconnectFocuserAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Focuser disconnection: {result.Value}");
}
```

### Focuser Control

#### `MoveFocuserAsync(int position)`
Moves the focuser to the specified absolute position.

**Parameters:**
- `position`: The absolute position to move the focuser to

**Returns:** `Result<string>`

**Example:**
```csharp
var result = await ninaClient.MoveFocuserAsync(15000);
if (result.IsSuccessful)
{
    Console.WriteLine($"Move command: {result.Value}");
}
```

#### `AutoFocusAsync(bool? cancel = null)`
Starts an autofocus operation or cancels a running autofocus if cancel is true.

**Parameters:**
- `cancel` (optional): Set to true to cancel a running autofocus operation

**Returns:** `Result<string>`

**Example:**
```csharp
// Start autofocus
var result = await ninaClient.AutoFocusAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Autofocus: {result.Value}");
}

// Cancel running autofocus
var cancelResult = await ninaClient.AutoFocusAsync(cancel: true);
```

#### `GetLastAutoFocusAsync()`
Gets detailed information about the last autofocus operation, including focus points, measurements, and curve fitting data.

**Returns:** `Result<FocuserLastAF>`

**Example:**
```csharp
var result = await ninaClient.GetLastAutoFocusAsync();
if (result.IsSuccessful)
{
    var af = result.Value;
    Console.WriteLine($"Filter: {af.Filter}");
    Console.WriteLine($"Method: {af.Method}");
    Console.WriteLine($"Duration: {af.Duration}");
    Console.WriteLine($"Temperature: {af.Temperature}°C");
    Console.WriteLine($"Final Position: {af.CalculatedFocusPoint.Position}");
    Console.WriteLine($"HFR Value: {af.CalculatedFocusPoint.Value}");
}
```

## FocuserInfo Properties

The `FocuserInfo` object contains the following properties:

- `Position` (int): Current focuser position in steps
- `StepSize` (int): Size of each step in microns
- `Temperature` (double): Current temperature reading in Celsius
- `IsMoving` (bool): Whether the focuser is currently moving
- `IsSettling` (bool): Whether the focuser is settling after a move
- `TempComp` (bool): Whether temperature compensation is enabled
- `TempCompAvailable` (bool): Whether temperature compensation is available
- `SupportedActions` (List<object>): List of supported ASCOM actions

## FocuserLastAF Properties

The `FocuserLastAF` object contains comprehensive autofocus result data:

### Basic Information
- `Version` (int): Autofocus data format version
- `Filter` (string): Filter used during autofocus
- `AutoFocuserName` (string): Name of the autofocus routine
- `StarDetectorName` (string): Star detection method used
- `Timestamp` (string): When the autofocus was performed
- `Temperature` (double): Temperature during autofocus
- `Method` (string): Autofocus method (e.g., "STARHFR")
- `Fitting` (string): Curve fitting method used
- `Duration` (string): Total duration of autofocus operation

### Focus Points
- `InitialFocusPoint`: Starting focus position and HFR
- `CalculatedFocusPoint`: Final calculated optimal focus position
- `PreviousFocusPoint`: Previous known good focus position
- `MeasurePoints`: Array of all measurement points taken during autofocus

### Analysis Data
- `Intersections`: Trend line and hyperbolic minimum intersections
- `Fittings`: Curve fitting equations (quadratic, hyperbolic, gaussian, trends)
- `RSquares`: R-squared values for each curve fitting method
- `BacklashCompensation`: Backlash compensation settings used

## Error Handling

All focuser methods return `Result<T>` objects that encapsulate success/failure states:

```csharp
var result = await ninaClient.ConnectFocuserAsync();
if (result.IsSuccessful)
{
    // Use result.Value
    Console.WriteLine($"Connected: {result.Value}");
}
else
{
    // Handle error
    Console.WriteLine($"Error: {result.Error?.Message}");
}
```

## Common Use Cases

### Complete Focusing Workflow
```csharp
// 1. Connect to focuser
var connectResult = await ninaClient.ConnectFocuserAsync();
if (!connectResult.IsSuccessful) return;

// 2. Get current position
var infoResult = await ninaClient.GetFocuserInfoAsync();
if (infoResult.IsSuccessful)
{
    Console.WriteLine($"Current position: {infoResult.Value.Position}");
}

// 3. Perform autofocus
var afResult = await ninaClient.AutoFocusAsync();
if (!afResult.IsSuccessful) return;

// 4. Wait for completion and get results
await Task.Delay(30000); // Wait for autofocus to complete
var lastAF = await ninaClient.GetLastAutoFocusAsync();
if (lastAF.IsSuccessful)
{
    Console.WriteLine($"Optimal position: {lastAF.Value.CalculatedFocusPoint.Position}");
}
```

### Manual Focus Adjustment
```csharp
// Get current position
var info = await ninaClient.GetFocuserInfoAsync();
if (info.IsSuccessful)
{
    int currentPos = info.Value.Position;
    
    // Move inward by 500 steps
    var moveResult = await ninaClient.MoveFocuserAsync(currentPos - 500);
    if (moveResult.IsSuccessful)
    {
        Console.WriteLine("Moved focuser inward");
    }
}
```

## API Endpoints

The focuser methods correspond to these NINA API endpoints:

- `GET /v2/api/equipment/focuser/info` - Get focuser information
- `GET /v2/api/equipment/focuser/list-devices` - List available devices  
- `GET /v2/api/equipment/focuser/rescan` - Rescan for devices
- `GET /v2/api/equipment/focuser/connect[?to=deviceId]` - Connect to focuser
- `GET /v2/api/equipment/focuser/disconnect` - Disconnect focuser
- `GET /v2/api/equipment/focuser/move?position={position}` - Move focuser
- `GET /v2/api/equipment/focuser/auto-focus[?cancel=true]` - Start/cancel autofocus
- `GET /v2/api/equipment/focuser/last-af` - Get last autofocus result

All endpoints follow the standard NINA API response format with `Success`, `Error`, `StatusCode`, and `Response` fields.
