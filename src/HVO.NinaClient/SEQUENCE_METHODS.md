# NINA API Sequence Methods

This document describes the sequence methods available in the NINA API client.

## Overview

The sequence methods allow you to control and interact with NINA's sequence functionality. These methods enable you to:
- Get sequence data in JSON format
- Edit sequence properties
- Start, stop, and reset sequences
- Load sequences from files or JSON
- Set sequence targets
- List available sequences

## Requirements

- The sequencer view must be initialized (can be achieved by opening the sequencer tab once in NINA)
- For most operations, a deep sky object (DSO) container must be present in the sequence

## Available Methods

### GetSequenceJsonAsync
Gets the current sequence as JSON. This is the recommended method for retrieving sequence data.

```csharp
Task<Result<SequenceJsonResponse>> GetSequenceJsonAsync(CancellationToken cancellationToken = default)
```

### GetSequenceStateAsync
Gets the complete sequence state with more detailed information and plugin support. Use this as reference for sequence editing operations.

```csharp
Task<Result<SequenceJsonResponse>> GetSequenceStateAsync(CancellationToken cancellationToken = default)
```

### EditSequenceAsync
Edits sequence properties using a path-based approach similar to profile editing.

```csharp
Task<Result<string>> EditSequenceAsync(string path, string value, CancellationToken cancellationToken = default)
```

**Parameters:**
- `path`: Property path using format like "Imaging-Items-0-Items-0-ExposureTime"
- `value`: New value as string

**Note:** This mainly supports simple types (strings, numbers) and may not work with complex objects or enums.

### StartSequenceAsync
Starts the sequence execution.

```csharp
Task<Result<string>> StartSequenceAsync(bool? skipValidation = null, CancellationToken cancellationToken = default)
```

**Parameters:**
- `skipValidation`: Optional parameter to skip sequence validation

### StopSequenceAsync
Stops the currently running sequence.

```csharp
Task<Result<string>> StopSequenceAsync(CancellationToken cancellationToken = default)
```

### ResetSequenceAsync
Resets the sequence to its initial state.

```csharp
Task<Result<string>> ResetSequenceAsync(CancellationToken cancellationToken = default)
```

### ListAvailableSequencesAsync
Lists available sequence files from the default sequence folders.

```csharp
Task<Result<AvailableSequencesResponse>> ListAvailableSequencesAsync(CancellationToken cancellationToken = default)
```

**Note:** Currently limited utility as sequence loading functionality is restricted.

### SetSequenceTargetAsync
Sets the target coordinates for a specific target container in the sequence.

```csharp
Task<Result<string>> SetSequenceTargetAsync(
    string name, 
    double ra, 
    double dec, 
    double rotation, 
    int index, 
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `name`: Target name (e.g., "M31")
- `ra`: Right ascension in degrees
- `dec`: Declination in degrees  
- `rotation`: Target rotation angle
- `index`: Index of the target container to update (0-based)

### LoadSequenceFromFileAsync
Loads a sequence from a file in the default sequence folders.

```csharp
Task<Result<string>> LoadSequenceFromFileAsync(string sequenceName, CancellationToken cancellationToken = default)
```

**Parameters:**
- `sequenceName`: Name of the sequence file to load

### LoadSequenceFromJsonAsync
Loads a sequence from JSON data provided by the client.

```csharp
Task<Result<string>> LoadSequenceFromJsonAsync(string sequenceJson, CancellationToken cancellationToken = default)
```

**Parameters:**
- `sequenceJson`: Complete sequence JSON data as string

## Error Handling

Common error scenarios:
- **409 Conflict**: Sequencer not initialized, no DSO container found, sequence already running
- **400 Bad Request**: Invalid parameters, sequence validation failed
- **500 Internal Server Error**: NINA internal errors

## Example Usage

```csharp
// Get current sequence
var sequenceResult = await ninaClient.GetSequenceJsonAsync();
if (sequenceResult.IsSuccess)
{
    var sequence = sequenceResult.Value;
    // Process sequence data
}

// Start sequence
var startResult = await ninaClient.StartSequenceAsync(skipValidation: false);
if (startResult.IsSuccess)
{
    Console.WriteLine($"Sequence started: {startResult.Value}");
}

// Set target coordinates
var targetResult = await ninaClient.SetSequenceTargetAsync(
    name: "M31", 
    ra: 10.68458, 
    dec: 41.26917, 
    rotation: 0.0, 
    index: 0);

// Edit sequence property
var editResult = await ninaClient.EditSequenceAsync(
    path: "Imaging-Items-0-Items-0-ExposureTime", 
    value: "30");
```

## Models

### SequenceJsonResponse
Contains the sequence data with items, conditions, triggers, and status information.

### AvailableSequencesResponse  
Contains the list of available sequence files with event and time information.

### SequenceItem
Represents individual sequence items (instructions or containers) with conditions, items, triggers, status, and name.

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/v2/api/sequence/json` | Get sequence as JSON |
| GET | `/v2/api/sequence/state` | Get complete sequence state |
| GET | `/v2/api/sequence/edit` | Edit sequence property |
| GET | `/v2/api/sequence/start` | Start sequence |
| GET | `/v2/api/sequence/stop` | Stop sequence |
| GET | `/v2/api/sequence/reset` | Reset sequence |
| GET | `/v2/api/sequence/list-available` | List available sequences |
| GET | `/v2/api/sequence/set-target` | Set sequence target |
| GET | `/v2/api/sequence/load` | Load sequence from file |
| POST | `/v2/api/sequence/load` | Load sequence from JSON |
