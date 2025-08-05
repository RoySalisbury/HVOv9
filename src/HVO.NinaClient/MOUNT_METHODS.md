# NINA API Mount Equipment Methods

This document describes the Mount equipment methods implemented in the NINA API client, following the NINA Advanced API v2.2.6 specification.

## Overview

The Mount equipment methods provide comprehensive control over telescope mount operations including connection management, slewing, parking, tracking modes, and synchronization functionality.

## Methods

### Connection Management

#### GetMountInfoAsync()
```csharp
Task<Result<MountInfo>> GetMountInfoAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/info`
- **Purpose**: Retrieves detailed information about the currently connected mount
- **Returns**: `MountInfo` object containing mount status, capabilities, and current state
- **Usage**: Check mount connection status, position, tracking state, and capabilities

#### GetMountDevicesAsync()
```csharp
Task<Result<IReadOnlyList<DeviceInfo>>> GetMountDevicesAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/list-devices`
- **Purpose**: Lists all available mount devices that can be connected
- **Returns**: Read-only list of `DeviceInfo` objects
- **Usage**: Discover available mount drivers before connecting

#### RescanMountDevicesAsync()
```csharp
Task<Result<IReadOnlyList<DeviceInfo>>> RescanMountDevicesAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/rescan`
- **Purpose**: Rescans for new mount devices and returns updated list
- **Returns**: Updated read-only list of `DeviceInfo` objects
- **Usage**: Refresh device list if new hardware was connected

#### ConnectMountAsync(string? deviceId = null)
```csharp
Task<Result<string>> ConnectMountAsync(string? deviceId = null, CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/connect`
- **Parameters**:
  - `deviceId` (optional): Specific device ID to connect to
- **Returns**: Connection status message (e.g., "Connected")
- **Usage**: Establish connection to mount. If no deviceId provided, connects to default/previously selected device

#### DisconnectMountAsync()
```csharp
Task<Result<string>> DisconnectMountAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/disconnect`
- **Returns**: Disconnection status message (e.g., "Disconnected")
- **Usage**: Safely disconnect from the mount

### Mount Operations

#### HomeMountAsync()
```csharp
Task<Result<string>> HomeMountAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/home`
- **Returns**: Homing operation status
- **Possible Responses**: "Homing", "Mount already homed"
- **Error Conditions**: Mount not connected, mount parked
- **Usage**: Initialize mount to home position

#### SetMountTrackingModeAsync(int mode)
```csharp
Task<Result<string>> SetMountTrackingModeAsync(int mode, CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/tracking`
- **Parameters**:
  - `mode`: Tracking mode (0: Sidereal, 1: Lunar, 2: Solar, 3: King, 4: Stopped)
- **Returns**: Tracking mode change status
- **Error Conditions**: Mount not connected/parked, invalid tracking mode
- **Usage**: Set how the mount tracks celestial objects

### Parking Operations

#### ParkMountAsync()
```csharp
Task<Result<string>> ParkMountAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/park`
- **Returns**: Park operation status
- **Possible Responses**: "Parking", "Mount already parked"
- **Error Conditions**: Mount not connected
- **Usage**: Move mount to park position for safety

#### UnparkMountAsync()
```csharp
Task<Result<string>> UnparkMountAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/unpark`
- **Returns**: Unpark operation status
- **Possible Responses**: "Unparking", "Mount not parked"
- **Error Conditions**: Mount not connected
- **Usage**: Remove mount from park position to enable operations

#### SetMountParkPositionAsync()
```csharp
Task<Result<string>> SetMountParkPositionAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/set-park-position`
- **Returns**: Set park position operation status
- **Requirements**: Mount must be unparked
- **Error Conditions**: Mount not connected, mount can't set park position, park position update failed
- **Usage**: Define current position as the new park position

### Movement Operations

#### FlipMountAsync()
```csharp
Task<Result<string>> FlipMountAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/flip`
- **Returns**: Meridian flip operation status
- **Behavior**: Only flips if needed, won't force unnecessary flip
- **Error Conditions**: Mount not connected/parked
- **Usage**: Perform meridian flip when required

#### SlewMountAsync(double ra, double dec, ...)
```csharp
Task<Result<string>> SlewMountAsync(
    double ra,
    double dec,
    bool? waitForResult = null,
    bool? center = null,
    bool? rotate = null,
    double? rotationAngle = null,
    CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/slew`
- **Parameters**:
  - `ra`: Right ascension in degrees (required)
  - `dec`: Declination in degrees (required)
  - `waitForResult`: Wait until slew completes
  - `center`: Center telescope on target
  - `rotate`: Perform center and rotate
  - `rotationAngle`: Rotation angle in degrees
- **Returns**: Slew operation status
- **Possible Responses**: "Started Slew", "Slew finished", "Slew failed"
- **Error Conditions**: Mount not connected/parked
- **Usage**: Move mount to specific coordinates with optional centering/rotation

#### StopMountSlewAsync()
```csharp
Task<Result<string>> StopMountSlewAsync(CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/slew/stop`
- **Returns**: Stop slew operation status
- **Note**: Most useful for simple slews without center/rotate operations
- **Error Conditions**: Mount not connected
- **Usage**: Emergency stop of mount movement

### Synchronization

#### SyncMountAsync(double? ra = null, double? dec = null)
```csharp
Task<Result<string>> SyncMountAsync(double? ra = null, double? dec = null, CancellationToken cancellationToken = default)
```
- **Endpoint**: `/v2/api/equipment/mount/sync`
- **Parameters**:
  - `ra` (optional): Right ascension in degrees
  - `dec` (optional): Declination in degrees
- **Returns**: Sync operation status
- **Behavior**: If coordinates omitted, performs platesolve for sync
- **Error Conditions**: Mount not connected/parked
- **Usage**: Synchronize mount position with actual sky coordinates

## Error Handling

All mount methods use the `Result<T>` pattern for consistent error handling:

```csharp
var result = await ninaClient.GetMountInfoAsync();
if (result.IsSuccessful)
{
    var mountInfo = result.Value;
    Console.WriteLine($"Mount: {mountInfo.Name}, Connected: {mountInfo.Connected}");
}
else
{
    Console.WriteLine($"Error: {result.Error?.Message}");
}
```

### Common Error Conditions
- **Mount not connected**: Most operations require active mount connection
- **Mount parked**: Many operations are disabled when mount is parked
- **Invalid parameters**: Tracking modes, coordinates must be within valid ranges
- **Hardware limitations**: Some operations depend on mount capabilities

## Example Usage

### Basic Mount Connection
```csharp
// Get available devices
var devicesResult = await ninaClient.GetMountDevicesAsync();
if (devicesResult.IsSuccessful)
{
    var device = devicesResult.Value.FirstOrDefault();
    if (device != null)
    {
        // Connect to first available device
        var connectResult = await ninaClient.ConnectMountAsync(device.Id);
        if (connectResult.IsSuccessful)
        {
            Console.WriteLine($"Connected: {connectResult.Value}");
        }
    }
}
```

### Slewing to Target
```csharp
// Slew to M31 (example coordinates)
var slewResult = await ninaClient.SlewMountAsync(
    ra: 10.68470833,  // RA in degrees
    dec: 41.26875,    // Dec in degrees
    waitForResult: true,
    center: true);

if (slewResult.IsSuccessful)
{
    Console.WriteLine($"Slew status: {slewResult.Value}");
}
```

### Safe Shutdown Sequence
```csharp
// Stop any movement
await ninaClient.StopMountSlewAsync();

// Set tracking to stopped
await ninaClient.SetMountTrackingModeAsync(4); // 4 = Stopped

// Park the mount
var parkResult = await ninaClient.ParkMountAsync();
if (parkResult.IsSuccessful)
{
    Console.WriteLine("Mount parked safely");
}

// Disconnect
await ninaClient.DisconnectMountAsync();
```

## API Compatibility

- **NINA Version**: Requires NINA Advanced API v2.2.6 or later
- **Thread Safety**: All methods are thread-safe and async
- **Cancellation**: All methods support cancellation tokens
- **Logging**: Comprehensive structured logging using ILogger<T>

## Notes

- Mount operations require careful attention to safety
- Always park mount before disconnecting
- Use appropriate tracking modes for different observation types
- Consider mount limitations when setting parameters
- Monitor mount status through GetMountInfoAsync() for state changes
