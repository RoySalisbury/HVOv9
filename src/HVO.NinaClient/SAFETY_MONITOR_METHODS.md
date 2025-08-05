# NINA API Safety Monitor Equipment Methods

This document describes the Safety Monitor equipment methods implemented in the NINA API client, following the NINA Advanced API v2.2.6 specification.

## Overview

Safety monitors are critical observatory safety devices that provide real-time monitoring of environmental conditions. The safety monitor methods provide complete device management and monitoring capabilities for automated observatory systems.

## Methods

### Information and Status

#### `GetSafetyMonitorInfoAsync()`
Gets detailed information about the currently connected safety monitor device.

**Endpoint:** `/v2/api/equipment/safetymonitor/info`  
**Parameters:** None  
**Returns:** `Result<SafetyMonitorInfoResponse>` containing device information and safety status  

**Key Properties:**
- `IsSafe`: Boolean indicating current safety status
- `SupportedActions`: List of supported device actions
- Standard device information (Name, Description, DisplayName, etc.)

### Device Management

#### `ConnectSafetyMonitorAsync(string? deviceId = null)`
Connects to a safety monitor device.

**Endpoint:** `/v2/api/equipment/safetymonitor/connect`  
**Parameters:**
- `deviceId` (optional): Specific device ID to connect to
**Returns:** `Result<string>` with connection status message

#### `DisconnectSafetyMonitorAsync()`
Disconnects from the currently connected safety monitor.

**Endpoint:** `/v2/api/equipment/safetymonitor/disconnect`  
**Parameters:** None  
**Returns:** `Result<string>` with disconnection status message

### Device Discovery

#### `GetSafetyMonitorDevicesAsync()`
Gets a list of all available safety monitor devices that can be connected to.

**Endpoint:** `/v2/api/equipment/safetymonitor/list-devices`  
**Parameters:** None  
**Returns:** `Result<IReadOnlyList<DeviceInfo>>` containing available devices

#### `RescanSafetyMonitorDevicesAsync()`
Rescans for available safety monitor devices and returns updated device list.

**Endpoint:** `/v2/api/equipment/safetymonitor/rescan`  
**Parameters:** None  
**Returns:** `Result<IReadOnlyList<DeviceInfo>>` containing updated device list

## Usage Examples

### Basic Safety Monitor Connection

```csharp
// Get list of available devices
var devicesResult = await ninaClient.GetSafetyMonitorDevicesAsync();
if (devicesResult.IsSuccessful)
{
    foreach (var device in devicesResult.Value)
    {
        Console.WriteLine($"Available Safety Monitor: {device.Name} ({device.Id})");
    }
}

// Connect to default safety monitor
var connectResult = await ninaClient.ConnectSafetyMonitorAsync();
if (connectResult.IsSuccessful)
{
    Console.WriteLine($"Connected: {connectResult.Value}");
}

// Check safety status
var infoResult = await ninaClient.GetSafetyMonitorInfoAsync();
if (infoResult.IsSuccessful)
{
    var safetyInfo = infoResult.Value;
    Console.WriteLine($"Safety Status: {(safetyInfo.Response.IsSafe ? "SAFE" : "UNSAFE")}");
    Console.WriteLine($"Device: {safetyInfo.Response.Name}");
}
```

### Connect to Specific Device

```csharp
// Rescan for new devices
var rescanResult = await ninaClient.RescanSafetyMonitorDevicesAsync();
if (rescanResult.IsSuccessful)
{
    var devices = rescanResult.Value;
    if (devices.Count > 0)
    {
        // Connect to first available device
        var connectResult = await ninaClient.ConnectSafetyMonitorAsync(devices[0].Id);
        if (connectResult.IsSuccessful)
        {
            Console.WriteLine($"Connected to {devices[0].Name}");
        }
    }
}
```

### Safety Monitoring Loop

```csharp
public async Task<bool> CheckSafetyStatusAsync()
{
    var result = await ninaClient.GetSafetyMonitorInfoAsync();
    if (result.IsSuccessful)
    {
        var isSafe = result.Value.Response.IsSafe;
        if (!isSafe)
        {
            logger.LogWarning("UNSAFE CONDITIONS DETECTED - Safety monitor triggered");
            // Implement emergency procedures
        }
        return isSafe;
    }
    
    logger.LogError("Failed to get safety status: {Error}", result.Error);
    return false; // Assume unsafe if we can't get status
}
```

## Error Handling

All methods use the `Result<T>` pattern for consistent error handling:

```csharp
var result = await ninaClient.GetSafetyMonitorInfoAsync();
if (result.IsSuccessful)
{
    // Success case
    var safetyInfo = result.Value;
    var isSafe = safetyInfo.Response.IsSafe;
}
else
{
    // Error case
    var error = result.Error;
    logger.LogError(error, "Safety monitor operation failed");
}
```

## Safety Monitor Integration Best Practices

1. **Continuous Monitoring**: Check safety status regularly in automated sequences
2. **Emergency Response**: Always implement emergency procedures for unsafe conditions
3. **Fallback Logic**: Assume unsafe conditions if device communication fails
4. **Device Validation**: Verify device connection before starting critical operations
5. **Logging**: Log all safety state changes for observatory audit trails

## API Endpoints

All endpoints follow the standard NINA API response format with `Success`, `Error`, `StatusCode`, and `Response` fields.

- `/v2/api/equipment/safetymonitor/info` - Get device information and safety status
- `/v2/api/equipment/safetymonitor/connect` - Connect to safety monitor device
- `/v2/api/equipment/safetymonitor/disconnect` - Disconnect from device  
- `/v2/api/equipment/safetymonitor/list-devices` - List available devices
- `/v2/api/equipment/safetymonitor/rescan` - Rescan for new devices

## API Compatibility

- **NINA Version**: Requires NINA Advanced API v2.2.6 or later
- **Thread Safety**: All methods are thread-safe and async
- **Cancellation**: All methods support cancellation tokens
- **Logging**: Comprehensive structured logging using ILogger<T>

## Notes

- Safety monitors are critical safety devices - always handle connection failures gracefully
- The `IsSafe` property is the primary indicator for observatory safety status
- Device rescanning may take several seconds depending on hardware configuration
- Always disconnect devices when shutting down the observatory system
- Monitor safety status continuously during automated operations
