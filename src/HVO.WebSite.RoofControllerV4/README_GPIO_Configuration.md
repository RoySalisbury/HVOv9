# GPIO Configuration Guide

## Overview

The RoofController application supports both real Raspberry Pi GPIO hardware and mock GPIO controllers for development and testing.

## Configuration

The GPIO controller type is determined by the `USE_REAL_GPIO` environment variable:

- **`USE_REAL_GPIO=true`**: Uses real Raspberry Pi GPIO hardware (`GpioControllerWrapper`)
- **`USE_REAL_GPIO=false`** or **unset**: Uses mock GPIO controller (`MockGpioController`)

## Setting Environment Variables

### Windows (Command Prompt)
```cmd
set USE_REAL_GPIO=true
dotnet run
```

### Windows (PowerShell)
```powershell
$env:USE_REAL_GPIO="true"
dotnet run
```

### Linux/Raspberry Pi
```bash
export USE_REAL_GPIO=true
dotnet run
```

## Development vs Production

The application always uses the real `RoofController` business logic. The only difference is which GPIO controller implementation is used:

- **`USE_REAL_GPIO=false`** or **unset**: Uses `MockGpioController` that emulates hardware behavior
- **`USE_REAL_GPIO=true`**: Uses real Raspberry Pi GPIO hardware (`GpioControllerWrapper`)

This approach provides several benefits:
- Real business logic testing in all environments
- Hardware-realistic behavior through `MockGpioController`
- Seamless transition between development and production
- No need for separate mock implementations of business logic

## GPIO Pin Configuration

The GPIO pins used by the roof controller are configured in `appsettings.json`:

```json
{
  "RoofControllerOptions": {
    "CloseRoofRelayPin": 0,
    "OpenRoofRelayPin": 0,
    "StopRoofRelayPin": 0,
    "KeypadEnableRelayPin": 0,
    "RoofClosedLimitSwitchPin": 21,
    "RoofOpenedLimitSwitchPin": 17,
    "StopRoofButtonPin": 0,
    "OpenRoofButtonPin": 0,
    "CloseRoofButtonPin": 0
  }
}
```

## Testing

For unit tests and integration tests, the `MockGpioController` provides:
- Hardware-realistic pin initialization behavior
- Event simulation capabilities
- No hardware dependencies
- Consistent test results

## Hardware Requirements

When using real GPIO hardware (`USE_REAL_GPIO=true`):
- **Linux/Raspberry Pi only** - Real GPIO hardware is only supported on Linux platforms
- Raspberry Pi 4 or 5
- Proper GPIO pin connections as defined in configuration
- Run with appropriate permissions (typically requires `sudo` or GPIO group membership)

**Important**: Setting `USE_REAL_GPIO=true` on Windows or other non-Linux platforms will throw a `PlatformNotSupportedException` at application startup.

## Platform Support

| Platform | Mock GPIO (`USE_REAL_GPIO=false`) | Real GPIO (`USE_REAL_GPIO=true`) |
|----------|-----------------------------------|----------------------------------|
| Windows  | ✅ Supported                     | ❌ Not Supported                |
| macOS    | ✅ Supported                     | ❌ Not Supported                |
| Linux    | ✅ Supported                     | ✅ Supported                    |
| Raspberry Pi | ✅ Supported                 | ✅ Supported                    |
