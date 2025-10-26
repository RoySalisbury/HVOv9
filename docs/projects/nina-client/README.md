# HVO NINA Client Project Guide

This guide covers the design, implementation, and usage of the HVO.NinaClient library for integrating with N.I.N.A. (Nighttime Imaging 'N' Astronomy).

## Project Overview

The HVO.NinaClient provides a .NET wrapper around NINA's REST and WebSocket APIs, enabling HVOv9 to remotely control NINA equipment and monitor imaging sessions.

## Architecture

### API Client Structure

```
HVO.NinaClient/
├── NinaApiClient.cs          # Main API client class
├── Models/                   # Response/request DTOs
│   ├── CameraInfo.cs
│   ├── MountInfo.cs
│   └── SequenceStatus.cs
├── Endpoints/                # API endpoint constants
└── Exceptions/               # NINA-specific exceptions
```

### Official API Specifications

The client implementation follows the official NINA API specifications:

- **REST API**: https://github.com/christian-photo/ninaAPI/blob/main/ninaAPI/api_spec.yaml
- **WebSocket API**: https://github.com/christian-photo/ninaAPI/blob/main/ninaAPI/websocket_spec.yaml

## Implementation Details

### Result Pattern Usage

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

### Equipment Connection Management

```csharp
// Connect all equipment
public async Task<Result<string>> ConnectAllAsync()
{
    var results = await Task.WhenAll(
        ConnectCameraAsync(),
        ConnectMountAsync(),
        ConnectFocuserAsync()
    );
    
    var failures = results.Where(r => !r.IsSuccess).ToArray();
    if (failures.Any())
    {
        var message = string.Join(", ", failures.Select(f => f.Error.Message));
        return Result<string>.Failure(new Exception($"Connection failures: {message}"));
    }
    
    return Result<string>.Success("All equipment connected");
}
```

### Status Monitoring

```csharp
public async Task<Result<CameraInfo>> GetCameraInfoAsync()
{
    try
    {
        var response = await _httpClient.GetAsync("/api/v1/camera/info");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        var info = JsonSerializer.Deserialize<CameraInfo>(json);
        
        return Result<CameraInfo>.Success(info);
    }
    catch (Exception ex)
    {
        return Result<CameraInfo>.Failure(ex);
    }
}
```

## Integration Patterns

### Observatory Coordination

```csharp
public class ObservatoryController
{
    private readonly NinaApiClient _ninaClient;
    private readonly RoofController _roofController;
    private readonly WeatherService _weatherService;
    
    public async Task<Result> StartImagingSessionAsync()
    {
        // 1. Check weather conditions
        var weather = await _weatherService.GetCurrentConditionsAsync();
        if (!weather.IsSuccess || !weather.Value.IsSafeForImaging)
        {
            return Result.Failure(new Exception("Weather conditions unsafe for imaging"));
        }
        
        // 2. Open roof
        var roofResult = await _roofController.OpenRoofAsync();
        if (!roofResult.IsSuccess)
        {
            return Result.Failure(new Exception("Failed to open roof"));
        }
        
        // 3. Connect NINA equipment
        var connectResult = await _ninaClient.ConnectAllAsync();
        if (!connectResult.IsSuccess)
        {
            await _roofController.CloseRoofAsync(); // Rollback
            return Result.Failure(new Exception("Failed to connect equipment"));
        }
        
        // 4. Start imaging sequence
        return await _ninaClient.StartSequenceAsync();
    }
}
```

### Safety Integration

```csharp
public class SafetyMonitor
{
    private readonly NinaApiClient _ninaClient;
    private readonly WeatherService _weatherService;
    private readonly Timer _monitorTimer;
    
    public SafetyMonitor(NinaApiClient ninaClient, WeatherService weatherService)
    {
        _ninaClient = ninaClient;
        _weatherService = weatherService;
        _monitorTimer = new Timer(CheckSafetyConditions, null, TimeSpan.Zero, TimeSpan.FromMinutes(2));
    }
    
    private async void CheckSafetyConditions(object? state)
    {
        try
        {
            var conditions = await _weatherService.GetCurrentConditionsAsync();
            if (!conditions.IsSuccess) return;
            
            // Check abort conditions
            if (conditions.Value.CloudCover > 80 ||
                conditions.Value.WindSpeed > 25 ||
                conditions.Value.Humidity > 85)
            {
                _logger.LogWarning("Safety abort triggered: {Conditions}", conditions.Value);
                
                // Emergency stop
                await _ninaClient.StopSequenceAsync();
                await _ninaClient.ParkMountAsync();
                await _ninaClient.DisconnectAllAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking safety conditions");
        }
    }
}
```

## Configuration

### Dependency Injection Setup

```csharp
// Program.cs
builder.Services.Configure<NinaOptions>(
    builder.Configuration.GetSection("Nina"));

builder.Services.AddSingleton<NinaApiClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<NinaOptions>>().Value;
    return new NinaApiClient(options.BaseUrl, sp.GetRequiredService<ILogger<NinaApiClient>>());
});
```

### Configuration Options

```json
{
  "Nina": {
    "BaseUrl": "http://localhost:1888",
    "ApiVersion": "v1",
    "TimeoutSeconds": 30,
    "RetryCount": 3,
    "RetryDelaySeconds": 2
  }
}
```

## Testing

### Unit Testing with Mock HTTP

```csharp
[TestMethod]
public async Task ConnectCameraAsync_Success_ReturnsConnectedStatus()
{
    // Arrange
    var mockHandler = new MockHttpMessageHandler();
    mockHandler.When("*/api/v1/camera/connect")
               .Respond("application/json", "\"Connected\"");
    
    var httpClient = new HttpClient(mockHandler);
    var client = new NinaApiClient("http://localhost:1888", httpClient);
    
    // Act
    var result = await client.ConnectCameraAsync();
    
    // Assert
    Assert.IsTrue(result.IsSuccess);
    Assert.AreEqual("Connected", result.Value);
}
```

### Integration Testing

```csharp
[TestMethod]
public async Task IntegrationTest_RealNinaInstance()
{
    // Requires NINA running on localhost:1888
    var client = new NinaApiClient("http://localhost:1888");
    
    var info = await client.GetApplicationInfoAsync();
    Assert.IsTrue(info.IsSuccess);
    Assert.IsNotNull(info.Value.Version);
}
```

## WebSocket Integration (Future)

### Event Subscriptions

```csharp
public class NinaWebSocketClient : IDisposable
{
    private ClientWebSocket? _webSocket;
    
    public event EventHandler<SequenceProgressEventArgs>? SequenceProgress;
    public event EventHandler<ExposureCompleteEventArgs>? ExposureComplete;
    
    public async Task ConnectAsync(string wsUrl)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        
        _ = Task.Run(ListenForMessages);
    }
    
    private async Task ListenForMessages()
    {
        var buffer = new byte[4096];
        
        while (_webSocket?.State == WebSocketState.Open)
        {
            var result = await _webSocket.ReceiveAsync(buffer, CancellationToken.None);
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            
            await ProcessMessageAsync(message);
        }
    }
}
```

## API Coverage

### Currently Implemented

- ✅ Equipment connection/disconnection (camera, mount, focuser)
- ✅ Equipment status queries
- ✅ Basic sequence control (start/stop)
- ✅ Application info retrieval

### Planned Enhancements

- [ ] WebSocket event subscriptions
- [ ] Advanced sequence control
- [ ] Flat panel automation
- [ ] Autofocus integration
- [ ] Meridian flip monitoring
- [ ] Weather integration
- [ ] Plate solving support

## Error Handling

### NINA-Specific Errors

```csharp
public class NinaConnectionException : Exception
{
    public string Equipment { get; }
    
    public NinaConnectionException(string equipment, string message) 
        : base($"NINA {equipment} connection failed: {message}")
    {
        Equipment = equipment;
    }
}
```

### Retry Logic

```csharp
private async Task<Result<T>> RetryAsync<T>(Func<Task<Result<T>>> operation, int maxRetries = 3)
{
    Exception? lastException = null;
    
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            var result = await operation();
            if (result.IsSuccess) return result;
            
            lastException = result.Error;
        }
        catch (Exception ex)
        {
            lastException = ex;
        }
        
        if (attempt < maxRetries - 1)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        }
    }
    
    return Result<T>.Failure(lastException ?? new Exception("Operation failed after retries"));
}
```

## Performance Considerations

### Connection Pooling

```csharp
public class NinaApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _rateLimitSemaphore;
    
    public NinaApiClient(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _rateLimitSemaphore = new SemaphoreSlim(5, 5); // Max 5 concurrent requests
    }
    
    private async Task<T> ExecuteWithRateLimitAsync<T>(Func<Task<T>> operation)
    {
        await _rateLimitSemaphore.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _rateLimitSemaphore.Release();
        }
    }
}
```

## Related Documentation

- [NINA Official Documentation](https://nighttime-imaging.eu/docs/)
- [Observatory Automation Architecture](../observatory-automation.md)
- [HVO.NinaClient API Reference](../../src/HVO.NINA/README.md)