# HVO.WebSite - Observatory Web Applications

[![WebSite CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/website.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/website.yml)

> **🚧 WORK IN PROGRESS - Active Development**
>
> This domain contains the main observatory website and development playground. The architecture is being actively refined as new features are added.

## 📦 Domain Overview

The **HVO.WebSite** domain provides:
- **Observatory dashboard** - Real-time monitoring of equipment, weather, sky conditions
- **Historical data visualization** - Weather trends, sky quality archives, imaging logs
- **Remote control interfaces** - Roof control, camera settings, equipment status
- **Public information** - Observatory location, equipment specs, image gallery
- **Development playground** - Testing ground for new Blazor components and features

## 📁 Projects in This Domain

### HVO.WebSite.v9
Main observatory website (Blazor Server):
- Real-time equipment dashboard
- Weather station integration
- Sky monitor visualization
- Roof controller status
- NINA equipment monitoring
- Historical data charts
- REST API for external access
- Docker deployment support

### HVO.WebSite.Playground
Development and testing site (Blazor Server):
- Component development sandbox
- UI theme testing
- API endpoint prototyping
- Performance testing
- Feature experimentation before production deployment

### HVO.WebSite.Playground.Tests
Integration tests for web applications:
- API endpoint testing
- Component rendering validation
- Authentication/authorization tests
- Database integration tests

## 🔑 Current Features

### Real-Time Equipment Dashboard

```razor
@page "/dashboard"
@inject WeatherService Weather
@inject RoofController Roof
@inject NinaApiClient Nina
@inject SkyMonitorService SkyMonitor

<PageTitle>Observatory Dashboard - HVO</PageTitle>

<div class="dashboard-grid">
    <!-- Weather Panel -->
    <div class="dashboard-panel weather-panel">
        <h3>Weather Conditions</h3>
        <WeatherDisplay Data="@_weatherData" />
    </div>
    
    <!-- Roof Status Panel -->
    <div class="dashboard-panel roof-panel">
        <h3>Roof Status</h3>
        <RoofStatusDisplay State="@_roofState" />
    </div>
    
    <!-- Sky Monitor Panel -->
    <div class="dashboard-panel sky-panel">
        <h3>Sky Conditions</h3>
        <SkyMonitorDisplay 
            StarCount="@_starCount" 
            CloudPercentage="@_cloudPercentage" />
    </div>
    
    <!-- Equipment Status Panel -->
    <div class="dashboard-panel equipment-panel">
        <h3>Imaging Equipment (NINA)</h3>
        <EquipmentStatusDisplay Connected="@_ninaConnected" />
    </div>
</div>

@code {
    private WeatherData? _weatherData;
    private RoofState? _roofState;
    private int _starCount;
    private double _cloudPercentage;
    private bool _ninaConnected;
    
    protected override async Task OnInitializedAsync()
    {
        // Subscribe to real-time updates
        Weather.DataUpdated += OnWeatherUpdated;
        Roof.StateChanged += OnRoofStateChanged;
        SkyMonitor.ImageCaptured += OnSkyImageCaptured;
        
        // Load initial data
        await LoadInitialDataAsync();
    }
    
    private async Task LoadInitialDataAsync()
    {
        var weatherResult = await Weather.GetLatestAsync();
        if (weatherResult.IsSuccess)
            _weatherData = weatherResult.Value;
        
        _roofState = Roof.CurrentState;
        
        var skyStats = await SkyMonitor.GetLatestStatsAsync();
        if (skyStats.IsSuccess)
        {
            _starCount = skyStats.Value.StarCount;
            _cloudPercentage = skyStats.Value.CloudPercentage;
        }
        
        var ninaStatus = await Nina.GetConnectionStatusAsync();
        _ninaConnected = ninaStatus.IsSuccess && ninaStatus.Value.Connected;
    }
}
```

### REST API Endpoints

```csharp
[ApiController]
[Route("api/v1/observatory")]
public class ObservatoryApiController : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var status = new ObservatoryStatus
        {
            Timestamp = DateTime.UtcNow,
            Weather = await _weather.GetLatestAsync(),
            Roof = _roof.CurrentState.ToString(),
            Sky = await _skyMonitor.GetLatestStatsAsync(),
            Equipment = await _nina.GetEquipmentStatusAsync()
        };
        
        return Ok(status);
    }
    
    [HttpGet("weather/history")]
    public async Task<IActionResult> GetWeatherHistory(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end)
    {
        var data = await _weatherRepository.GetRangeAsync(start, end);
        return Ok(data);
    }
}
```

## 🎓 Planned Features

### Phase 1: Core Dashboard (In Progress)
- [x] Real-time weather display
- [x] Roof status monitoring
- [ ] Sky monitor integration (partial)
- [ ] NINA equipment status
- [ ] Historical weather charts
- [ ] Mobile-responsive layout

### Phase 2: Advanced Visualization
- [ ] Interactive sky charts with detected stars
- [ ] Time-lapse video generation
- [ ] Cloud coverage heatmaps
- [ ] Equipment usage statistics
- [ ] Performance metrics dashboard

### Phase 3: User Features
- [ ] User authentication (read-only public, admin control)
- [ ] Image gallery with FITS metadata
- [ ] Observation planning tools
- [ ] Email/SMS alerts for weather changes
- [ ] Public API with rate limiting

### Phase 4: Automation Control
- [ ] Schedule imaging sessions
- [ ] Configure automatic roof closing rules
- [ ] Manage camera settings remotely
- [ ] Create custom alert rules

## 🧪 Testing

### Run Domain Tests
```bash
cd src/HVO.WebSite
dotnet test
```

### Run Integration Tests
```bash
dotnet test --filter "Category=Integration"
```

### Build Domain Solution
```bash
cd src/HVO.WebSite
dotnet build HVO.WebSite.sln
```

## 🐳 Docker Deployment

### Build Containers
```bash
cd src/HVO.WebSite
docker-compose build
```

### Run Locally
```bash
docker-compose up -d
```

Access:
- **v9 Website**: http://localhost:5002
- **Playground**: http://localhost:5136

## ⚙️ Configuration

### appsettings.json
```json
{
  "ConnectionStrings": {
    "ApplicationDb": "Data Source=/data/hvo-website.db",
    "WeatherDb": "Data Source=/data/weather-history.db"
  },
  "ExternalServices": {
    "RoofControllerUrl": "http://roofcontroller:8080",
    "SkyMonitorUrl": "http://skymonitor:8080",
    "NinaApiUrl": "http://localhost:1888"
  },
  "Website": {
    "SiteName": "Hualapai Valley Observatory",
    "PublicAccess": true,
    "RequireAuthentication": false
  }
}
```

## 🔗 Dependencies

- `HVO.DataModels` - Entity Framework contexts
- `HVO.WebSite.Themes` - Shared UI design system
- `HVO` - Core library (Result<T>, Option<T>)
- External services: RoofController, SkyMonitor, NINA

## 📚 Architecture

### Service Integration Pattern
```csharp
// Services registered in Program.cs
builder.Services.AddSingleton<WeatherService>();
builder.Services.AddSingleton<RoofController>();
builder.Services.AddSingleton<NinaApiClient>();
builder.Services.AddSingleton<SkyMonitorService>();

// Blazor components inject services
@inject WeatherService Weather

// Background services run continuously
public class WeatherPollingService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _weather.RefreshDataAsync();
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
```

## 🎨 Design System

Uses **HVO Dark** theme from `HVO.WebSite.Themes`:
- Dark-first design for nighttime observatory use
- CSS custom properties for consistent styling
- Bootstrap 5.3 base with custom overrides
- Responsive grid layout
- Mobile-optimized controls

See: [HVO.WebSite.Themes](../HVO.WebSite.Themes/README.md)

## 🔄 Development Status

### Completed
- ✅ Basic Blazor Server setup
- ✅ HVO Dark theme integration
- ✅ Weather service integration
- ✅ Docker containerization
- ✅ CI/CD pipeline

### In Progress
- ⏳ Dashboard component development
- ⏳ Historical data visualization
- ⏳ Sky monitor integration
- ⏳ NINA equipment monitoring

### Planned
- 📅 User authentication
- 📅 Image gallery
- 📅 API documentation (OpenAPI)
- 📅 Mobile app (MAUI)

## 📖 Related Documentation

- [HVO.WebSite.Playground Project](HVO.WebSite.Playground/README.md)
- [HVO.WebSite.v9 Project](HVO.WebSite.v9/README.md)
- [HVO Dark Theme](../HVO.WebSite.Themes/README.md)
- [Blazor Component Best Practices](../../docs/guides/blazor-component-best-practices.md)

## 💡 Contributing

Website development is active and evolving. To contribute:
1. Use **HVO.WebSite.Playground** for experimentation
2. Follow Blazor component best practices (see docs)
3. Test with HVO Dark theme
4. Ensure mobile responsiveness
5. Submit PRs with screenshots

**Priority Areas**:
- Historical data chart components
- Sky monitor visualization improvements
- Mobile UI optimization
- API endpoint expansion
