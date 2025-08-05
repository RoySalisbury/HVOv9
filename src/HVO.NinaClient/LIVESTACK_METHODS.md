# NINA Livestack API Methods

This document describes the implementation of NINA Livestack API methods in the HVO.NinaClient library, based on the NINA Advanced API v2.2.6 specification.

## Overview

The livestack methods provide control over the NINA Livestack plugin functionality, allowing users to start/stop livestacking and retrieve stacked images.

## Requirements

- NINA Livestack plugin version >= v1.0.0.9
- Plugin must be properly installed and configured in NINA

## Implemented Methods

### 1. StartLivestackAsync

Starts the livestack process.

```csharp
Task<Result<string>> StartLivestackAsync(CancellationToken cancellationToken = default)
```

**Parameters:**
- `cancellationToken`: Cancellation token for the async operation

**Returns:**
- `Result<string>`: Operation result containing start status message

**API Endpoints:**
- `GET /v2/api/livestack/start`

**Example Usage:**
```csharp
var result = await ninaClient.StartLivestackAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Livestack started: {result.Value}");
}
else
{
    Console.WriteLine($"Failed to start livestack: {result.Error?.Message}");
}
```

### 2. StopLivestackAsync

Stops the livestack process.

```csharp
Task<Result<string>> StopLivestackAsync(CancellationToken cancellationToken = default)
```

**Parameters:**
- `cancellationToken`: Cancellation token for the async operation

**Returns:**
- `Result<string>`: Operation result containing stop status message

**API Endpoints:**
- `GET /v2/api/livestack/stop`

**Example Usage:**
```csharp
var result = await ninaClient.StopLivestackAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Livestack stopped: {result.Value}");
}
else
{
    Console.WriteLine($"Failed to stop livestack: {result.Error?.Message}");
}
```

### 3. GetLivestackImageAsync

Retrieves the stacked image from the livestack plugin for a specific target and filter combination.

```csharp
Task<Result<string>> GetLivestackImageAsync(
    string target,
    string filter,
    bool? resize = null,
    int? quality = null,
    string? size = null,
    double? scale = null,
    bool? stream = null,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `target`: The target name (e.g., "M31", "Orion Nebula")
- `filter`: The filter name (e.g., "RGB", "Ha", "OIII")
- `resize`: Whether to resize the image (optional)
- `quality`: Image quality from 1 (worst) to 100 (best), -1 for PNG (optional)
- `size`: Image size in format "[width]x[height]", requires resize=true (optional)
- `scale`: Scale factor for the image, requires resize=true (optional)
- `stream`: Stream the image in JPG/PNG format instead of base64 (optional)
- `cancellationToken`: Cancellation token for the async operation

**Returns:**
- `Result<string>`: Base64 encoded image data

**API Endpoints:**
- `GET /v2/api/livestack/{target}/{filter}`

**Example Usage:**
```csharp
var result = await ninaClient.GetLivestackImageAsync("M31", "RGB", resize: true, quality: 80, size: "1024x768");
if (result.IsSuccessful)
{
    // result.Value contains base64 encoded image data
    var imageBytes = Convert.FromBase64String(result.Value);
    await File.WriteAllBytesAsync("m31_stacked.jpg", imageBytes);
}
else
{
    Console.WriteLine($"Failed to get livestack image: {result.Error?.Message}");
}
```

## Error Handling

All methods follow the standard HVO Result<T> pattern for error handling:

- **Success**: `Result.IsSuccessful` is true, data available in `Result.Value`
- **Failure**: `Result.IsSuccessful` is false, error details in `Result.Error`

Common error scenarios:
- Livestack plugin not installed or not enabled
- Plugin version < v1.0.0.9
- Network communication issues with NINA
- Invalid target or filter names
- NINA not running or API not available

## Important Notes

1. **Fault Tolerance**: The start/stop methods cannot fail even if the livestack plugin is not installed. They simply issue commands to NINA.

2. **Plugin Dependencies**: The `GetLivestackImageAsync` method requires the livestack plugin to be actively running and have processed images for the specified target/filter combination.

3. **Image Format**: Images are returned as base64 encoded strings by default. Use the `stream` parameter for direct binary streaming.

4. **URL Encoding**: Target and filter names are automatically URL-encoded to handle special characters and spaces.

5. **Thread Safety**: All methods are thread-safe and can be called concurrently.

## Implementation Details

The livestack methods are implemented in:
- **Interface**: `INinaApiClient.cs` - Method signatures and documentation
- **Implementation**: `NinaApiClient.cs` - HTTP client logic and parameter handling
- **Logging**: Structured logging using `ILogger<NinaApiClient>` for debugging and monitoring

The implementation uses the standard NINA API response wrapper format and follows the established patterns used by other equipment methods in the client library.
