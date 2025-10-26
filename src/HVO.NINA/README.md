# HVO.NINA - NINA API Client Integration

[![NINA Domain CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/nina.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/nina.yml)

Domain providing integration with **N.I.N.A. (Nighttime Imaging 'N' Astronomy)**, a popular open-source astrophotography automation suite.

## 📦 Domain Overview

The **HVO.NINA** domain enables HVOv9 to:
- **Remote control NINA** - Start/stop imaging sequences, connect/disconnect equipment
- **Monitor imaging progress** - Track exposure count, integration time, sequence status
- **Equipment coordination** - Synchronize roof state with NINA equipment connections
- **Safety integration** - Abort sequences on weather alerts or equipment faults

## 📁 Projects in This Domain

### HVO.NinaClient
HTTP client library for NINA's REST and WebSocket APIs:
- Equipment connection management (camera, mount, focuser, etc.)
- Imaging sequence control
- Equipment status queries
- Event subscriptions via WebSocket
- Result<T> pattern for error handling

## 🔑 Key Features

### Equipment Connection Control

```csharp
using HVO.NinaClient;

var client = new NinaApiClient(baseUrl: "http://localhost:1888");

// Connect camera
var result = await client.ConnectCameraAsync();
if (result.IsSuccess)
{
    Console.WriteLine($"Camera connected: {result.Value}");
}
else
{
    Console.WriteLine($"Connection failed: {result.Error.Message}");
}

// Disconnect all equipment
await client.DisconnectAllAsync();
```

### Equipment Status Queries

```csharp
// Get camera info
var cameraInfo = await client.GetCameraInfoAsync();
if (cameraInfo.IsSuccess)
{
    Console.WriteLine($"Camera: {cameraInfo.Value.Name}");
    Console.WriteLine($"Sensor: {cameraInfo.Value.SensorType}");
    Console.WriteLine($"Temperature: {cameraInfo.Value.Temperature}°C");
}

// Check mount position
var mountInfo = await client.GetMountInfoAsync();
if (mountInfo.IsSuccess)
{
    Console.WriteLine($"RA: {mountInfo.Value.RightAscension}");
    Console.WriteLine($"Dec: {mountInfo.Value.Declination}");
    Console.WriteLine($"Tracking: {mountInfo.Value.Tracking}");
}
```

### Imaging Sequence Control

```csharp
// Start imaging sequence
var startResult = await client.StartSequenceAsync();

// Monitor progress via WebSocket
client.SequenceProgress += (sender, e) =>
{
    Console.WriteLine($"Frame {e.Current}/{e.Total} - {e.Target}");
};

// Stop sequence on abort condition
if (weatherAlert)
{
    await client.StopSequenceAsync();
}
```

## 🎓 Usage Examples

### Roof/Equipment Safety Coordination

```csharp
public class ObservatoryController
{
    private readonly NinaApiClient _ninaClient;
    private readonly RoofController _roofController;
    
    public async Task<Result> StartImagingSessionAsync()
    {
        // 1. Open roof
        var roofResult = await _roofController.OpenRoofAsync();
        if (!roofResult.IsSuccess)
            return Result.Failure(new Exception("Failed to open roof"));
        
        // 2. Connect NINA equipment
        var connectResult = await _ninaClient.ConnectAllAsync();
        if (!connectResult.IsSuccess)
        {
            await _roofController.CloseRoofAsync(); // Rollback
            return Result.Failure(new Exception("Failed to connect equipment"));
        }
        
        // 3. Start sequence
        return await _ninaClient.StartSequenceAsync();
    }
    
    public async Task EmergencyShutdownAsync()
    {
        // 1. Stop imaging
        await _ninaClient.StopSequenceAsync();
        
        // 2. Park mount
        await _ninaClient.ParkMountAsync();
        
        // 3. Disconnect equipment
        await _ninaClient.DisconnectAllAsync();
        
        // 4. Close roof
        await _roofController.CloseRoofAsync();
    }
}
```

### Weather-Triggered Abort

```csharp
public class WeatherMonitor
{
    private readonly NinaApiClient _ninaClient;
    private readonly WeatherService _weather;
    
    public async Task MonitorWeatherAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var conditions = await _weather.GetCurrentConditionsAsync();
            
            if (conditions.IsSuccess)
            {
                // Check abort conditions
                if (conditions.Value.CloudCover > 80 ||
                    conditions.Value.WindSpeed > 25 ||
                    conditions.Value.Humidity > 85)
                {
                    _logger.LogWarning("Weather abort triggered: {Conditions}", conditions.Value);
                    
                    // Stop NINA sequence
                    await _ninaClient.StopSequenceAsync();
                    await _ninaClient.ParkMountAsync();
                }
            }
            
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
```

### Blazor UI Integration

```razor
@inject NinaApiClient NinaClient
@implements IDisposable

<div class="nina-status">
    <h3>NINA Equipment Status</h3>
    
    <div class="equipment-grid">
        <div class="equipment-item">
            <span>Camera:</span>
            <span class="@(cameraConnected ? "text-success" : "text-muted")">
                @(cameraConnected ? "Connected" : "Disconnected")
            </span>
        </div>
        
        <div class="equipment-item">
            <span>Mount:</span>
            <span class="@(mountConnected ? "text-success" : "text-muted")">
                @(mountConnected ? "Connected" : "Disconnected")
            </span>
        </div>
    </div>
    
    <button class="btn btn-primary" @onclick="ConnectAllEquipment">
        Connect All
    </button>
</div>

@code {
    private bool cameraConnected;
    private bool mountConnected;
    private Timer? _statusTimer;
    
    protected override void OnInitialized()
    {
        _statusTimer = new Timer(async _ => await UpdateStatusAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }
    
    private async Task UpdateStatusAsync()
    {
        var cameraStatus = await NinaClient.GetCameraStatusAsync();
        var mountStatus = await NinaClient.GetMountStatusAsync();
        
        cameraConnected = cameraStatus.IsSuccess && cameraStatus.Value.Connected;
        mountConnected = mountStatus.IsSuccess && mountStatus.Value.Connected;
        
        await InvokeAsync(StateHasChanged);
    }
    
    private async Task ConnectAllEquipment()
    {
        await NinaClient.ConnectAllAsync();
        await UpdateStatusAsync();
    }
    
    public void Dispose()
    {
        _statusTimer?.Dispose();
    }
}
```

## 🧪 Testing

### Run Domain Tests
```bash
cd src/HVO.NINA
dotnet test
```

### Build Domain Solution
```bash
cd src/HVO.NINA
dotnet build HVO.NINA.sln
```

## ⚙️ Configuration

### appsettings.json
```json
{
  "Nina": {
    "BaseUrl": "http://localhost:1888",
    "ApiVersion": "v1",
    "TimeoutSeconds": 30,
    "RetryCount": 3
  }
}
```

### Dependency Injection
```csharp
// Program.cs
builder.Services.AddSingleton<NinaApiClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Nina:BaseUrl"] ?? "http://localhost:1888";
    return new NinaApiClient(baseUrl);
});
```

## 🔗 Dependencies

- `HVO` - Core library (Result<T>, Option<T>)
- `System.Net.Http.Json` - JSON serialization
- `Microsoft.Extensions.Logging.Abstractions` - Structured logging

## 📚 Used By

- `HVO.WebSite.v9` - Observatory dashboard with NINA status
- Future: Automated imaging orchestration, weather integration

## 📖 Official NINA API Specifications

**REST API Specification:**  
https://github.com/christian-photo/ninaAPI/blob/main/ninaAPI/api_spec.yaml

**WebSocket/AsyncAPI Specification:**  
https://github.com/christian-photo/ninaAPI/blob/main/ninaAPI/websocket_spec.yaml

## 🎨 Design Patterns

### Result<T> for API Calls
All API methods return `Result<T>` for consistent error handling:
```csharp
public async Task<Result<string>> ConnectCameraAsync()
{
    try
    {
        var response = await _httpClient.PostAsync("/api/v1/camera/connect", null);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadAsStringAsync();
        return Result<string>.Success(status);
    }
    catch (Exception ex)
    {
        return Result<string>.Failure(ex);
    }
}
```

### Retry Logic for Transient Failures
```csharp
private async Task<Result<T>> RetryAsync<T>(Func<Task<Result<T>>> operation, int maxRetries = 3)
{
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        var result = await operation();
        if (result.IsSuccess) return result;
        
        if (attempt < maxRetries - 1)
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }
    
    return Result<T>.Failure(new Exception($"Operation failed after {maxRetries} attempts"));
}
```

## 🔄 Future Enhancements

- [ ] Add WebSocket event subscriptions (SequenceProgress, ExposureComplete)
- [ ] Implement equipment connection state caching
- [ ] Add NINA plugin development support
- [ ] Create NINA Advanced Sequencer integration
- [ ] Add flat panel automation support
- [ ] Implement meridian flip monitoring
- [ ] Add autofocus event tracking

## 📖 Related Documentation

- [NINA Official Site](https://nighttime-imaging.eu/)
- [NINA API Documentation](https://github.com/christian-photo/ninaAPI)
- [HVO NINA Client Project Guide](../../docs/projects/nina-client/)
- [Observatory Automation Architecture](../../docs/observatory-automation.md)
