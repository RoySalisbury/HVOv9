# NINA Profile API Methods

This document describes the implementation of NINA Advanced API profile methods in HVO.NinaClient.

## Overview

The profile methods provide access to NINA's profile management system, allowing you to view, modify, and switch between profiles programmatically.

## Implemented Methods

### 1. ShowProfileAsync

Shows profile information - either the active profile or a list of all available profiles.

**Method Signature:**
```csharp
Task<Result<object>> ShowProfileAsync(bool? active = null, CancellationToken cancellationToken = default)
```

**Parameters:**
- `active` (optional): 
  - `true` - Returns the active profile details
  - `false` - Returns a list of all available profiles
  - `null` - Returns the active profile details (default behavior)

**API Endpoint:** `GET /v2/api/profile/show?active={active}`

**Example Usage:**
```csharp
// Get active profile details
var activeProfile = await client.ShowProfileAsync(active: true);

// Get list of all profiles
var allProfiles = await client.ShowProfileAsync(active: false);

// Get active profile (default)
var profile = await client.ShowProfileAsync();
```

**Response:** Returns a complex object containing profile configuration data including:
- Name, Description, Id, LastUsed
- ApplicationSettings (Culture, DevicePollingInterval, PageSize, LogLevel)
- CameraSettings (Id, PixelSize, Gain, Offset, etc.)
- AutoFocusSettings (exposure times, step sizes, methods, etc.)
- FramingAssistantSettings
- GuiderSettings (PHD2 configuration, planetarium settings)
- PlateSolveSettings (solver configurations, API keys)
- RotatorSettings
- FlatDeviceSettings
- ImageHistorySettings

### 2. ChangeProfileValueAsync

Changes a specific value in the active profile.

**Method Signature:**
```csharp
Task<Result<string>> ChangeProfileValueAsync(string settingPath, object newValue, CancellationToken cancellationToken = default)
```

**Parameters:**
- `settingPath`: The path to the setting to change (e.g., "CameraSettings-PixelSize"). Use dash (-) to separate nested objects
- `newValue`: The new value to set

**API Endpoint:** `GET /v2/api/profile/change-value?settingpath={settingPath}&newValue={newValue}`

**Example Usage:**
```csharp
// Change camera pixel size
await client.ChangeProfileValueAsync("CameraSettings-PixelSize", 3.2);

// Change autofocus exposure time
await client.ChangeProfileValueAsync("AutoFocusSettings-AutoFocusExposureTime", 2.0);

// Change application culture
await client.ChangeProfileValueAsync("ApplicationSettings-Culture", "en-US");
```

**Response:** Returns a success message like "Updated setting"

### 3. SwitchProfileAsync

Switches to a different profile.

**Method Signature:**
```csharp
Task<Result<string>> SwitchProfileAsync(string profileId, CancellationToken cancellationToken = default)
```

**Parameters:**
- `profileId`: The ID of the profile to switch to (obtained from ShowProfileAsync with active=false)

**API Endpoint:** `GET /v2/api/profile/switch?profileid={profileId}`

**Example Usage:**
```csharp
// First, get list of available profiles
var profilesResult = await client.ShowProfileAsync(active: false);
if (profilesResult.IsSuccessful)
{
    // Assuming we have a list of profiles, get the ID of the desired profile
    var profiles = profilesResult.Value as IEnumerable<dynamic>;
    var targetProfileId = "some-profile-id";
    
    // Switch to the target profile
    var switchResult = await client.SwitchProfileAsync(targetProfileId);
}
```

**Response:** Returns a success message like "Successfully switched profile"

### 4. GetProfileHorizonAsync

Gets the horizon data for the active profile.

**Method Signature:**
```csharp
Task<Result<HorizonData>> GetProfileHorizonAsync(CancellationToken cancellationToken = default)
```

**API Endpoint:** `GET /v2/api/profile/horizon`

**Example Usage:**
```csharp
var horizonResult = await client.GetProfileHorizonAsync();
if (horizonResult.IsSuccessful)
{
    var horizon = horizonResult.Value;
    Console.WriteLine($"Horizon points: {horizon.Altitudes.Count}");
    
    for (int i = 0; i < horizon.Azimuths.Count; i++)
    {
        Console.WriteLine($"Azimuth: {horizon.Azimuths[i]}°, Altitude: {horizon.Altitudes[i]}°");
    }
}
```

**Response:** Returns `HorizonData` containing:
- `Altitudes`: Array of altitude values in degrees
- `Azimuths`: Array of azimuth values in degrees

## Error Handling

All methods follow the standard HVO `Result<T>` pattern for consistent error handling:

```csharp
var result = await client.ShowProfileAsync(active: true);
if (result.IsSuccessful)
{
    // Use result.Value
    var profile = result.Value;
}
else
{
    // Handle error
    Console.WriteLine($"Error: {result.Error?.Message}");
}
```

## Common Error Scenarios

### ShowProfileAsync
- HTTP 500: Internal server error

### ChangeProfileValueAsync
- HTTP 400: Invalid path or new value can't be null
- HTTP 500: Internal server error

### SwitchProfileAsync
- HTTP 409: Profile not found ("No profile with specified id found!")
- HTTP 500: Internal server error

### GetProfileHorizonAsync
- HTTP 500: Internal server error

## Model Classes

### ProfileInfo
Main profile information class containing all profile settings sections.

### HorizonData
Contains arrays of altitude and azimuth values representing the horizon profile.

### Response Wrappers
- `ProfileInfoResponse`: Wrapper for single profile data
- `ProfileListResponse`: Wrapper for profile list data
- `HorizonDataResponse`: Wrapper for horizon data

## Logging

All methods include structured logging with appropriate log levels:
- `LogDebug`: For retrieving information
- `LogInformation`: For operations that modify state
- `LogError`: For failures (handled by base GetAsync method)

## Thread Safety

All methods are thread-safe and can be called concurrently. The underlying HTTP client handles concurrent requests appropriately.

## NINA API Compliance

This implementation follows the NINA Advanced API v2.2.6 specification:
- Correct endpoint URLs
- Proper parameter handling with URL encoding
- Consistent response handling through the Result<T> pattern
- Comprehensive error handling for all documented error scenarios
