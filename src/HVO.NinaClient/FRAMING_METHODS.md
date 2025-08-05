# NINA Framing Assistant Methods

This document describes the framing assistant methods available in the NINA API client.

## Overview

The framing assistant methods provide comprehensive control over NINA's framing assistant functionality, allowing you to set image sources, coordinates, rotation, and perform slewing operations. These methods are compliant with the NINA API v2.2.6 specification.

## Available Methods

### Information

#### `GetFramingAssistantInfoAsync()`
Gets comprehensive information about the current framing assistant setup.

**Returns:** `Result<FramingAssistantInfo>`

**Example:**
```csharp
var result = await ninaClient.GetFramingAssistantInfoAsync();
if (result.IsSuccessful)
{
    var info = result.Value;
    Console.WriteLine($"Source: {info.Source}");
    Console.WriteLine($"Coordinates: RA {info.RightAscension}°, Dec {info.Declination}°");
    Console.WriteLine($"Rotation: {info.Rotation}°");
    Console.WriteLine($"Field of View: {info.FoVW}° × {info.FoVH}°");
    Console.WriteLine($"DSO: {info.DSO}");
}
```

### Configuration

#### `SetFramingAssistantSourceAsync(string source)`
Sets the image source for the framing assistant.

**Parameters:**
- `source`: The image source to use (e.g., "NASA", "SKYSERVER", "STSCI", "ESO")

**Returns:** `Result<string>`

**Example:**
```csharp
var result = await ninaClient.SetFramingAssistantSourceAsync("NASA");
if (result.IsSuccessful)
{
    Console.WriteLine($"Source set: {result.Value}");
}
```

#### `SetFramingAssistantCoordinatesAsync(double rightAscension, double declination)`
Sets the target coordinates for the framing assistant.

**Parameters:**
- `rightAscension`: Right ascension in degrees
- `declination`: Declination in degrees

**Returns:** `Result<string>`

**Example:**
```csharp
// Set coordinates for M31 (Andromeda Galaxy)
var result = await ninaClient.SetFramingAssistantCoordinatesAsync(10.6847, 41.2692);
if (result.IsSuccessful)
{
    Console.WriteLine($"Coordinates set: {result.Value}");
}
```

#### `SetFramingAssistantRotationAsync(double rotation)`
Sets the rotation angle for the framing assistant.

**Parameters:**
- `rotation`: Rotation angle in degrees

**Returns:** `Result<string>`

**Example:**
```csharp
var result = await ninaClient.SetFramingAssistantRotationAsync(45.0);
if (result.IsSuccessful)
{
    Console.WriteLine($"Rotation set: {result.Value}");
}
```

### Operations

#### `SlewFramingAssistantAsync(string? option = null)`
Slews the mount to the current framing assistant coordinates.

**Parameters:**
- `option` (optional): Slew option such as "Center" or "Rotate"

**Returns:** `Result<string>`

**Example:**
```csharp
// Basic slew
var result = await ninaClient.SlewFramingAssistantAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Slew initiated: {result.Value}");
}

// Slew with center option
var centerResult = await ninaClient.SlewFramingAssistantAsync("Center");
if (centerResult.IsSuccessful)
{
    Console.WriteLine($"Center slew initiated: {centerResult.Value}");
}
```

#### `DetermineFramingAssistantRotationAsync()`
Determines the rotation angle from the current camera image.

**Returns:** `Result<string>`

**Example:**
```csharp
var result = await ninaClient.DetermineFramingAssistantRotationAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Rotation determined: {result.Value}");
}
```

## FramingAssistantInfo Properties

The `FramingAssistantInfo` object contains comprehensive framing setup information:

### Basic Information
- `Source`: Current image source (NASA, SKYSERVER, etc.)
- `DSO`: Deep Sky Object name or identifier

### Coordinates
- `RightAscension`: Right ascension in degrees
- `Declination`: Declination in degrees
- `Rotation`: Current rotation angle in degrees

### Field of View
- `FoVW`: Field of view width in degrees
- `FoVH`: Field of view height in degrees

### Camera Settings
- `CameraPixelX`: Camera pixel count (X axis)
- `CameraPixelY`: Camera pixel count (Y axis)
- `CameraPixelSize`: Camera pixel size in micrometers

### Telescope Configuration
- `TelescopeFocalLength`: Telescope focal length in millimeters

## Common Use Cases

### Complete Framing Workflow
```csharp
// 1. Set image source
var sourceResult = await ninaClient.SetFramingAssistantSourceAsync("NASA");
if (!sourceResult.IsSuccessful) return;

// 2. Set target coordinates (M42 - Orion Nebula)
var coordResult = await ninaClient.SetFramingAssistantCoordinatesAsync(83.8221, -5.3911);
if (!coordResult.IsSuccessful) return;

// 3. Set desired rotation
var rotationResult = await ninaClient.SetFramingAssistantRotationAsync(0.0);
if (!rotationResult.IsSuccessful) return;

// 4. Slew to target
var slewResult = await ninaClient.SlewFramingAssistantAsync("Center");
if (!slewResult.IsSuccessful) return;

// 5. Get current framing info
var infoResult = await ninaClient.GetFramingAssistantInfoAsync();
if (infoResult.IsSuccessful)
{
    Console.WriteLine($"Framing setup complete for {infoResult.Value.DSO}");
}
```

### Automatic Rotation Determination
```csharp
// Set up target without rotation
await ninaClient.SetFramingAssistantCoordinatesAsync(310.3578, 40.2569); // M57 - Ring Nebula
await ninaClient.SlewFramingAssistantAsync();

// Take an image and determine rotation automatically
var rotationResult = await ninaClient.DetermineFramingAssistantRotationAsync();
if (rotationResult.IsSuccessful)
{
    Console.WriteLine($"Automatic rotation: {rotationResult.Value}");
    
    // Slew again with the determined rotation
    await ninaClient.SlewFramingAssistantAsync("Rotate");
}
```

### Image Source Comparison
```csharp
string[] sources = { "NASA", "SKYSERVER", "STSCI", "ESO" };

foreach (var source in sources)
{
    var result = await ninaClient.SetFramingAssistantSourceAsync(source);
    if (result.IsSuccessful)
    {
        var info = await ninaClient.GetFramingAssistantInfoAsync();
        if (info.IsSuccessful)
        {
            Console.WriteLine($"{source}: FoV {info.Value.FoVW:F2}° × {info.Value.FoVH:F2}°");
        }
    }
}
```

## Error Handling

All framing assistant methods return `Result<T>` objects that should be checked for success:

```csharp
var result = await ninaClient.SetFramingAssistantSourceAsync("NASA");
if (result.IsSuccessful)
{
    Console.WriteLine($"Success: {result.Value}");
}
else
{
    Console.WriteLine($"Failed: {result.Error?.Message}");
}
```

## Notes

- The framing assistant requires NINA to be running and properly configured
- Image sources may have different coverage and image quality
- Coordinates should be in J2000 epoch
- Rotation angles are in degrees (0-360)
- Some operations may require connected mount and camera equipment
