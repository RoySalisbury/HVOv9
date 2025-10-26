# HVO.RoofControllerV4 - Observatory Roll-Off Roof Control

[![Roof Controller V4 CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/roofcontroller.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/roofcontroller.yml)

Domain providing automated control and safety systems for the Hualapai Valley Observatory's roll-off roof, enabling remote operation and fail-safe protection of telescope equipment.

## 📦 Domain Overview

The **HVO.RoofControllerV4** domain delivers:
- **Safety-first automation** - Multi-layer safety interlocks prevent equipment damage
- **Remote web control** - Blazor UI for local network and iPad operation
- **Real-time monitoring** - Live roof position, limit switch states, motor status
- **Weather integration** - Automatic close on unsafe conditions
- **Fail-safe design** - Dead-man timer, emergency stop, power-loss protection

## 📁 Projects in This Domain

### HVO.RoofControllerV4.RPi
Main Blazor Server application for Raspberry Pi:
- Blazor interactive UI with real-time updates
- GPIO motor control (open/close/emergency stop)
- Limit switch monitoring (open/closed positions)
- Safety timer enforcement
- REST API for external control
- Docker deployment support

### HVO.RoofControllerV4.Common
Shared DTOs and models:
- API request/response models
- Roof state enumerations
- Safety configuration constants

### HVO.RoofControllerV4.RPi.Tests
Comprehensive integration and unit tests:
- Safety interlock verification
- Timer enforcement testing
- API endpoint validation
- Hardware simulation for CI/CD

## 🔑 Key Features

### Multi-Layer Safety System

```csharp
public class RoofSafetyController
{
    // Layer 1: Dead-man timer (auto-stop if no heartbeat)
    private readonly Timer _deadManTimer;
    
    // Layer 2: Motion timeout (stop if movement exceeds expected duration)
    private readonly Timer _motionTimer;
    
    // Layer 3: Limit switch verification
    private readonly GpioLimitSwitch _openLimit;
    private readonly GpioLimitSwitch _closedLimit;
    
    // Layer 4: Emergency stop input
    private readonly GpioPin _emergencyStopPin;
    
    public async Task<Result> OpenRoofAsync()
    {
        // Verify closed limit is active
        if (!_closedLimit.IsClosed)
            return Result.Failure(new InvalidOperationException("Roof not at closed position"));
        
        // Start dead-man timer
        _deadManTimer.Start(TimeSpan.FromSeconds(30));
        
        // Start motion timer
        _motionTimer.Start(TimeSpan.FromSeconds(60));
        
        // Activate motor
        _motorRelay.SetState(MotorDirection.Open);
        
        // Monitor until open limit reached or timeout
        while (!_openLimit.IsClosed && _motionTimer.IsRunning)
        {
            if (!_deadManTimer.IsRunning)
            {
                StopMotor();
                return Result.Failure(new TimeoutException("Dead-man timer expired"));
            }
            
            await Task.Delay(100);
        }
        
        StopMotor();
        return Result.Success();
    }
}
```

### Real-Time Blazor UI

```razor
@page "/roof-control"
@inject RoofController RoofController
@implements IDisposable

<div class="roof-control-panel">
    <h2>Roof Status: @RoofController.CurrentState</h2>
    
    <div class="limit-switches">
        <span class="@(RoofController.ClosedLimitActive ? "text-success" : "text-muted")">
            Closed Limit: @(RoofController.ClosedLimitActive ? "ACTIVE" : "Inactive")
        </span>
        <span class="@(RoofController.OpenLimitActive ? "text-success" : "text-muted")">
            Open Limit: @(RoofController.OpenLimitActive ? "ACTIVE" : "Inactive")
        </span>
    </div>
    
    <div class="control-buttons">
        <button class="btn btn-success" 
                @onclick="OpenRoof" 
                disabled="@(!CanOpen)">
            Open Roof
        </button>
        
        <button class="btn btn-primary" 
                @onclick="CloseRoof" 
                disabled="@(!CanClose)">
            Close Roof
        </button>
        
        <button class="btn btn-danger" 
                @onclick="EmergencyStop">
            EMERGENCY STOP
        </button>
    </div>
    
    @if (RoofController.IsMoving)
    {
        <div class="safety-timer">
            Dead-Man Timer: @RoofController.RemainingTimerSeconds seconds
        </div>
    }
</div>

@code {
    private Timer? _uiUpdateTimer;
    
    private bool CanOpen => RoofController.CurrentState == RoofState.Closed && !RoofController.IsMoving;
    private bool CanClose => RoofController.CurrentState == RoofState.Open && !RoofController.IsMoving;
    
    protected override void OnInitialized()
    {
        RoofController.StateChanged += OnRoofStateChanged;
        _uiUpdateTimer = new Timer(_ => InvokeAsync(StateHasChanged), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }
    
    private async void OnRoofStateChanged(object? sender, RoofStateChangedEventArgs e)
    {
        await InvokeAsync(StateHasChanged);
    }
    
    private async Task OpenRoof()
    {
        var result = await RoofController.OpenAsync();
        if (!result.IsSuccess)
        {
            // Show error toast
        }
    }
    
    private async Task CloseRoof()
    {
        var result = await RoofController.CloseAsync();
        if (!result.IsSuccess)
        {
            // Show error toast
        }
    }
    
    private void EmergencyStop()
    {
        RoofController.EmergencyStop();
    }
    
    public void Dispose()
    {
        RoofController.StateChanged -= OnRoofStateChanged;
        _uiUpdateTimer?.Dispose();
    }
}
```

### REST API for External Control

```csharp
[ApiController]
[Route("api/v1/roof")]
public class RoofApiController : ControllerBase
{
    private readonly RoofController _roofController;
    
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new RoofStatusResponse
        {
            State = _roofController.CurrentState.ToString(),
            IsMoving = _roofController.IsMoving,
            ClosedLimitActive = _roofController.ClosedLimitActive,
            OpenLimitActive = _roofController.OpenLimitActive,
            TimerRemaining = _roofController.RemainingTimerSeconds
        });
    }
    
    [HttpPost("open")]
    public async Task<IActionResult> OpenRoof()
    {
        var result = await _roofController.OpenAsync();
        return result.IsSuccess 
            ? Ok(new { message = "Roof opening" }) 
            : StatusCode(500, new { error = result.Error.Message });
    }
    
    [HttpPost("close")]
    public async Task<IActionResult> CloseRoof()
    {
        var result = await _roofController.CloseAsync();
        return result.IsSuccess 
            ? Ok(new { message = "Roof closing" }) 
            : StatusCode(500, new { error = result.Error.Message });
    }
    
    [HttpPost("stop")]
    public IActionResult EmergencyStop()
    {
        _roofController.EmergencyStop();
        return Ok(new { message = "Emergency stop activated" });
    }
}
```

## 🎓 Usage Examples

### Weather Integration

```csharp
public class WeatherSafetyMonitor : BackgroundService
{
    private readonly RoofController _roofController;
    private readonly WeatherService _weatherService;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var weather = await _weatherService.GetCurrentConditionsAsync();
            
            if (weather.IsSuccess)
            {
                // Check unsafe conditions
                if (weather.Value.CloudCover > 80 ||
                    weather.Value.WindSpeed > 25 ||
                    weather.Value.Precipitation > 0)
                {
                    _logger.LogWarning("Unsafe weather detected, closing roof");
                    await _roofController.CloseAsync();
                }
            }
            
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

### iPad Remote Control

The Blazor UI is optimized for iPad Safari:
- Touch-friendly button sizes (min 44×44 pt)
- Responsive layout for landscape/portrait
- Real-time updates via SignalR
- Dark mode for nighttime use
- Network autodiscovery via mDNS/Bonjour

## 🧪 Testing

### Run Domain Tests
```bash
cd src/HVO.RoofControllerV4
dotnet test
```

### Integration Tests with Hardware Simulation
```bash
dotnet test --filter "Category=Integration"
```

### Test Coverage
```bash
# Run tests with coverage collection (same as CI)
dotnet test --settings ../coverage.runsettings

# Coverage reports are generated in TestResults/*/coverage.cobertura.xml
# See main README for coverage badge setup
```

### Build Domain Solution
```bash
cd src/HVO.RoofControllerV4
dotnet build HVO.RoofControllerV4.sln
```

## 🐳 Docker Deployment

### Build Container
```bash
cd src/HVO.RoofControllerV4
docker build -t hvo-roofcontroller:latest -f HVO.RoofControllerV4.RPi/Dockerfile .
```

### Run on Raspberry Pi
```bash
docker run -d \
  --name roofcontroller \
  --privileged \
  -p 5000:8080 \
  -v /sys/class/gpio:/sys/class/gpio \
  hvo-roofcontroller:latest
```

### Docker Compose
```bash
docker-compose up -d
```

## ⚙️ Configuration

### appsettings.json
```json
{
  "RoofController": {
    "ClosedLimitPin": 17,
    "OpenLimitPin": 27,
    "MotorOpenPin": 22,
    "MotorClosePin": 23,
    "EmergencyStopPin": 24,
    "DeadManTimerSeconds": 30,
    "MotionTimeoutSeconds": 60,
    "EnableHardware": true
  }
}
```

### GPIO Pin Assignments
| Pin | Function | Type |
|-----|----------|------|
| 17 | Closed Limit Switch | Input (Pull-Up) |
| 27 | Open Limit Switch | Input (Pull-Up) |
| 22 | Motor Open Relay | Output |
| 23 | Motor Close Relay | Output |
| 24 | Emergency Stop | Input (Pull-Up) |

## 🔗 Dependencies

- `HVO.Iot.Devices` - GPIO limit switches and relays
- `HVO` - Core library (Result<T>, OneOf patterns)
- `HVO.WebSite.Themes` - Shared Blazor UI theme

## 📚 Used By

- iPad operators on local observatory network
- `HVO.WebSite.v9` - Remote monitoring dashboard
- Future: Automated imaging orchestration

## 🛡️ Safety Features

### Dead-Man Timer
Motor stops if no heartbeat received within 30 seconds:
```csharp
private void ResetDeadManTimer()
{
    _deadManTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
}

private void OnDeadManTimerExpired()
{
    _logger.LogWarning("Dead-man timer expired, stopping motor");
    EmergencyStop();
}
```

### Motion Timeout
Motor stops if movement exceeds expected duration (60s):
```csharp
private void OnMotionTimeout()
{
    _logger.LogError("Motion timeout exceeded, possible mechanical failure");
    EmergencyStop();
    _state = RoofState.CreateFaulted("Motion timeout - check for obstructions");
}
```

### Power-Loss Protection
Roof state persisted to disk, restores on reboot:
```csharp
public async Task SaveStateAsync()
{
    var state = new RoofStatePersistence
    {
        LastKnownState = _state.ToString(),
        Timestamp = DateTime.UtcNow
    };
    
    await File.WriteAllTextAsync("/data/roof-state.json", JsonSerializer.Serialize(state));
}
```

## 🔄 Future Enhancements

- [ ] Add rain sensor integration
- [ ] Implement gradual roof opening (partial open positions)
- [ ] Add motor current monitoring for obstruction detection
- [ ] Create mobile app (MAUI) for iOS/Android
- [ ] Add ASCOM Alpaca driver for TheSkyX integration
- [ ] Implement roof position encoder (absolute positioning)
- [ ] Add MQTT integration for home automation
- [ ] Create voice control via Siri/Google Assistant

## 📖 Related Documentation

- [RoofController V4 Docker Deployment](../../docs/roofcontrollerv4-docker.md)
- [RoofController V4 Project Guide](../../docs/projects/roof-controller-v4-rpi/)
- [GPIO Hardware Wiring Diagram](../../docs/projects/roof-controller-v4-rpi/wiring-diagram.md) *(if exists)*
- [Safety System Design](../../docs/projects/roof-controller-v4-rpi/safety-design.md) *(if exists)*
