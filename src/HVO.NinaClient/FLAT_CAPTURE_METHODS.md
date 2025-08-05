# NINA Flat Capture Methods

This document describes the newly implemented flat capture methods for the NINA API client, following the official NINA API v2.2.6 specification.

## Overview

The flat capture methods are part of the `/flats` category in the NINA API and provide comprehensive support for various flat frame capture techniques used in astrophotography.

## Implemented Methods

### 1. Sky Flats (`CaptureSkyFlatsAsync`)

Captures sky flats using the twilight sky as a light source. Requires camera and mount to be connected.

**Endpoint:** `/v2/api/flats/skyflat`

**Parameters:**
- `count` (required) - Number of flats to capture
- `minExposure` (optional) - Minimum exposure time in seconds
- `maxExposure` (optional) - Maximum exposure time in seconds
- `histogramMean` (optional) - Target histogram mean (0-1)
- `meanTolerance` (optional) - Histogram tolerance (0-1)
- `dither` (optional) - Whether to dither between exposures
- `filterId` (optional) - Filter to use
- `binning` (optional) - Camera binning (e.g., "2x2")
- `gain` (optional) - Camera gain
- `offset` (optional) - Camera offset

### 2. Auto Brightness Flats (`CaptureAutoBrightnessFlatsAsync`)

Captures flats using a flat panel with automatic brightness adjustment for a fixed exposure time.

**Endpoint:** `/v2/api/flats/auto-brightness`

**Parameters:**
- `count` (required) - Number of flats to capture
- `exposureTime` (required) - Fixed exposure time in seconds
- `minBrightness` (optional) - Minimum panel brightness (0-99)
- `maxBrightness` (optional) - Maximum panel brightness (1-100)
- `histogramMean` (optional) - Target histogram mean (0-1)
- `meanTolerance` (optional) - Histogram tolerance (0-1)
- `filterId` (optional) - Filter to use
- `binning` (optional) - Camera binning
- `gain` (optional) - Camera gain
- `offset` (optional) - Camera offset
- `keepClosed` (optional) - Keep panel closed after capture

### 3. Auto Exposure Flats (`CaptureAutoExposureFlatsAsync`)

Captures flats using a flat panel with automatic exposure adjustment for a fixed brightness.

**Endpoint:** `/v2/api/flats/auto-exposure`

**Parameters:**
- `count` (required) - Number of flats to capture
- `brightness` (required) - Fixed panel brightness (0-100)
- `minExposure` (optional) - Minimum exposure time in seconds
- `maxExposure` (optional) - Maximum exposure time in seconds
- `histogramMean` (optional) - Target histogram mean (0-1)
- `meanTolerance` (optional) - Histogram tolerance (0-1)
- `filterId` (optional) - Filter to use
- `binning` (optional) - Camera binning
- `gain` (optional) - Camera gain
- `offset` (optional) - Camera offset
- `keepClosed` (optional) - Keep panel closed after capture

### 4. Trained Dark Flats (`CaptureTrainedDarkFlatsAsync`)

Captures dark flats based on previous training data stored in NINA.

**Endpoint:** `/v2/api/flats/trained-dark-flat`

**Parameters:**
- `count` (required) - Number of dark flats to capture
- `filterId` (optional) - Filter to use
- `binning` (optional) - Camera binning
- `gain` (optional) - Camera gain
- `offset` (optional) - Camera offset
- `keepClosed` (optional) - Keep panel closed after capture

### 5. Trained Flats (`CaptureTrainedFlatsAsync`)

Captures flats based on previous training data stored in NINA.

**Endpoint:** `/v2/api/flats/trained-flat`

**Parameters:**
- `count` (required) - Number of flats to capture
- `filterId` (optional) - Filter to use
- `binning` (optional) - Camera binning
- `gain` (optional) - Camera gain
- `offset` (optional) - Camera offset
- `keepClosed` (optional) - Keep panel closed after capture

### 6. Flat Capture Status (`GetFlatCaptureStatusAsync`)

Returns the current status of any running flat capture process.

**Endpoint:** `/v2/api/flats/status`

**Returns:** `FlatCaptureStatus` object containing:
- `CompletedIterations` - Number of completed captures
- `TotalIterations` - Total number of captures planned
- `State` - Current state (Running or Finished)

### 7. Stop Flat Capture (`StopFlatCaptureAsync`)

Stops any currently running flat capture process.

**Endpoint:** `/v2/api/flats/stop`

## Usage Examples

```csharp
// Capture 10 sky flats with specific exposure range
var skyFlatsResult = await ninaClient.CaptureSkyFlatsAsync(
    count: 10,
    minExposure: 1.0,
    maxExposure: 30.0,
    histogramMean: 0.5,
    binning: "2x2"
);

// Capture auto brightness flats with 5-second exposures
var autoBrightnessResult = await ninaClient.CaptureAutoBrightnessFlatsAsync(
    count: 20,
    exposureTime: 5.0,
    minBrightness: 10,
    maxBrightness: 90,
    filterId: 1
);

// Check the status of a running flat capture
var statusResult = await ninaClient.GetFlatCaptureStatusAsync();
if (statusResult.IsSuccessful)
{
    var status = statusResult.Value;
    Console.WriteLine($"Progress: {status.CompletedIterations}/{status.TotalIterations} - {status.State}");
}

// Stop a running flat capture if needed
var stopResult = await ninaClient.StopFlatCaptureAsync();
```

## Error Handling

All methods return `Result<T>` objects following the HVOv9 coding standards:

- Check `IsSuccessful` property before accessing `Value`
- Handle errors through the `Error` property
- Methods include comprehensive logging for debugging

## API Compliance

These implementations are fully compliant with the NINA API v2.2.6 specification and include:

- Proper parameter validation and encoding
- Structured logging with appropriate log levels
- Consistent error handling patterns
- Full XML documentation for IntelliSense support
- Nullable parameter support for optional fields

## Dependencies

- `HVO` library for `Result<T>` pattern
- `HVO.NinaClient.Models` for response models
- `Microsoft.Extensions.Logging` for structured logging
- `System.Text.Json` for JSON serialization
