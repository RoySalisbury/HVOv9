# NINA API Switch Equipment Methods

This document describes the switch equipment methods available in the NINA API client for controlling switch devices.

## Overview

Switch devices in NINA allow you to control various external devices through digital and analog switch outputs. This is useful for controlling power outlets, relays, heaters, fans, and other equipment during automated imaging sessions.

## Available Methods

### GetSwitchInfoAsync()
Get detailed information about the currently connected switch device.

```csharp
var result = await ninaClient.GetSwitchInfoAsync();
if (result.IsSuccessful)
{
    var switchInfo = result.Value.Response;
    Console.WriteLine($"Switch Name: {switchInfo.Name}");
    Console.WriteLine($"Connected: {switchInfo.Connected}");
    
    // Display writable switches
    foreach (var writableSwitch in switchInfo.WritableSwitches ?? [])
    {
        Console.WriteLine($"Writable Switch {writableSwitch.Id}: {writableSwitch.Name}");
        Console.WriteLine($"  Current Value: {writableSwitch.Value}");
        Console.WriteLine($"  Range: {writableSwitch.Minimum} - {writableSwitch.Maximum}");
        Console.WriteLine($"  Step Size: {writableSwitch.StepSize}");
    }
    
    // Display readonly switches
    foreach (var readonlySwitch in switchInfo.ReadonlySwitches ?? [])
    {
        Console.WriteLine($"Readonly Switch {readonlySwitch.Id}: {readonlySwitch.Name}");
        Console.WriteLine($"  Current Value: {readonlySwitch.Value}");
    }
}
```

### ConnectSwitchAsync(deviceId?)
Connect to a switch device. If no deviceId is provided, connects to the default/selected device.

```csharp
// Connect to default switch device
var result = await ninaClient.ConnectSwitchAsync();

// Connect to specific switch device
var result = await ninaClient.ConnectSwitchAsync("ASCOM.Simulator.Switch");

if (result.IsSuccessful)
{
    Console.WriteLine($"Connection result: {result.Value}");
}
```

### DisconnectSwitchAsync()
Disconnect from the currently connected switch device.

```csharp
var result = await ninaClient.DisconnectSwitchAsync();
if (result.IsSuccessful)
{
    Console.WriteLine($"Disconnection result: {result.Value}");
}
```

### GetSwitchDevicesAsync()
Get a list of all available switch devices that can be connected to.

```csharp
var result = await ninaClient.GetSwitchDevicesAsync();
if (result.IsSuccessful)
{
    Console.WriteLine("Available switch devices:");
    foreach (var device in result.Value)
    {
        Console.WriteLine($"  {device.Id}: {device.Name} (Connected: {device.Connected})");
    }
}
```

### RescanSwitchDevicesAsync()
Rescan for new switch devices and get an updated list of available devices.

```csharp
var result = await ninaClient.RescanSwitchDevicesAsync();
if (result.IsSuccessful)
{
    Console.WriteLine("Available switch devices after rescan:");
    foreach (var device in result.Value)
    {
        Console.WriteLine($"  {device.Id}: {device.Name}");
    }
}
```

### SetSwitchValueAsync(index, value)
Set the value of a writable switch at the specified index.

```csharp
// Set switch at index 0 to value 1.0 (typically "on" for boolean switches)
var result = await ninaClient.SetSwitchValueAsync(0, 1.0);

// Set switch at index 1 to value 0.0 (typically "off" for boolean switches)
var result = await ninaClient.SetSwitchValueAsync(1, 0.0);

// Set switch at index 2 to an analog value (e.g., brightness level)
var result = await ninaClient.SetSwitchValueAsync(2, 75.5);

if (result.IsSuccessful)
{
    Console.WriteLine($"Set switch result: {result.Value}");
}
```

## Common Usage Patterns

### Basic Switch Control
```csharp
// Connect to switch device
await ninaClient.ConnectSwitchAsync();

// Get switch information to understand available switches
var switchInfo = await ninaClient.GetSwitchInfoAsync();

// Turn on a power outlet (assuming switch 0 controls power)
await ninaClient.SetSwitchValueAsync(0, 1.0);

// Set a dew heater to 50% power (assuming switch 1 controls heater)
await ninaClient.SetSwitchValueAsync(1, 50.0);

// Turn off the power outlet when done
await ninaClient.SetSwitchValueAsync(0, 0.0);

// Disconnect when finished
await ninaClient.DisconnectSwitchAsync();
```

### Error Handling
```csharp
var result = await ninaClient.SetSwitchValueAsync(0, 1.0);
if (!result.IsSuccessful)
{
    Console.WriteLine($"Failed to set switch: {result.Error?.Message}");
    
    // Check if it's a specific error
    if (result.Error?.Message?.Contains("Switch not connected") == true)
    {
        // Try to connect first
        await ninaClient.ConnectSwitchAsync();
        result = await ninaClient.SetSwitchValueAsync(0, 1.0);
    }
}
```

## API Endpoints

These methods correspond to the following NINA Advanced API v2.2.6 endpoints:

- `GET /v2/api/equipment/switch/info` - GetSwitchInfoAsync()
- `GET /v2/api/equipment/switch/connect[?to=deviceId]` - ConnectSwitchAsync()
- `GET /v2/api/equipment/switch/disconnect` - DisconnectSwitchAsync()
- `GET /v2/api/equipment/switch/list-devices` - GetSwitchDevicesAsync()
- `GET /v2/api/equipment/switch/rescan` - RescanSwitchDevicesAsync()
- `GET /v2/api/equipment/switch/set?index={index}&value={value}` - SetSwitchValueAsync()

## Response Models

The switch methods use the following response models:

- **SwitchInfoResponse**: Contains detailed switch device information
- **SwitchInfo**: Core switch information including writable and readonly switches
- **WritableSwitch**: Information about switches that can be controlled
- **ReadonlySwitch**: Information about switches that are read-only
- **DeviceInfo**: Basic device information for device listing methods

## Switch Types

NINA switch devices typically support two types of switches:

1. **Boolean Switches**: Digital on/off switches (value 0.0 = off, 1.0 = on)
2. **Analog Switches**: Variable value switches (value range defined by Minimum/Maximum properties)

Always check the switch's Minimum, Maximum, and StepSize properties to understand the valid value range before setting values.
