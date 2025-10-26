# HVO.SkyMonitorV5 - All-Sky Camera & Starfield Analysis

[![SkyMonitor V5 CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/skymonitor-v5.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/skymonitor-v5.yml)

High-performance all-sky camera monitoring system with real-time starfield detection, cloud coverage analysis, and WASM-based browser visualization. **Successor to SkyMonitorV4** with 10x performance improvements.

## 📦 Domain Overview

The **HVO.SkyMonitorV5** domain delivers:
- **All-sky imaging** - 30-second exposures capturing entire visible sky
- **Real-time star detection** - SkiaSharp-based image processing pipeline
- **Cloud coverage analysis** - Pixel-level cloud detection with coverage percentage
- **WASM viewer** - Interactive browser-based sky visualization
- **Historical archives** - FITS image storage with searchable metadata
- **Performance optimized** - Multi-threaded processing, SIMD acceleration

## 📁 Projects in This Domain

### HVO.SkyMonitorV5.RPi
Main Blazor Server application for Raspberry Pi 5:
- ZWO ASI camera integration (ASI183MM, ASI224MC, etc.)
- SkiaSharp image processing pipeline
- Star detection with configurable SNR thresholds
- Cloud coverage percentage calculation
- FITS file generation and archival
- Real-time Blazor UI with live sky view
- REST API for external monitoring
- Docker deployment support

### HVO.SkyMonitorV5.Data
Entity Framework Core data models:
- Sky image metadata (timestamp, exposure, star count, cloud %)
- Image archive locations
- Weather correlation data
- Time-series queries for historical analysis

### HVO.SkyMonitorV5.RPi.Benchmarks
BenchmarkDotNet performance testing:
- Image processing pipeline benchmarks
- Star detection algorithm comparisons
- Memory allocation profiling
- Cross-platform performance validation (RPi5, M2 Mac, x64)

### HVO.SkyMonitorV5.RPi.Stress
Long-running stress tests:
- 24-hour continuous operation validation
- Memory leak detection
- Thermal throttling monitoring
- Storage capacity testing

### HVO.SkyMonitorV5.RPi.Tests
Comprehensive unit and integration tests:
- Image processing algorithm validation
- Star detection accuracy testing
- FITS file generation verification
- API endpoint testing

## 🔑 Key Features

### Real-Time Star Detection

```csharp
public class StarDetectionService
{
    private readonly ILogger<StarDetectionService> _logger;
    
    public async Task<Result<StarDetectionResult>> DetectStarsAsync(
        string fitsFilePath, 
        double snrThreshold = 3.0)
    {
        try
        {
            // Load FITS image
            using var fits = FitsFile.Open(fitsFilePath);
            var pixels = fits.ReadImageData<ushort>();
            var (width, height) = fits.GetImageDimensions();
            
            // Convert to SkiaSharp bitmap for processing
            using var bitmap = ConvertToSkBitmap(pixels, width, height);
            
            // Apply noise reduction
            using var filtered = ApplyMedianFilter(bitmap, radius: 2);
            
            // Detect stars above SNR threshold
            var stars = DetectBrightPixels(filtered, snrThreshold);
            
            // Filter false positives (cosmic rays, hot pixels)
            var validStars = stars
                .Where(s => s.FWHM > 1.5 && s.FWHM < 8.0)
                .Where(s => s.Roundness > 0.6)
                .ToList();
            
            _logger.LogInformation(
                "Detected {StarCount} stars (SNR threshold: {Threshold})", 
                validStars.Count, 
                snrThreshold);
            
            return Result<StarDetectionResult>.Success(new StarDetectionResult
            {
                StarCount = validStars.Count,
                Stars = validStars,
                MeanBackground = CalculateBackgroundLevel(filtered),
                NoiseLevel = CalculateNoiseStdDev(filtered)
            });
        }
        catch (Exception ex)
        {
            return Result<StarDetectionResult>.Failure(ex);
        }
    }
}
```

### Cloud Coverage Analysis

```csharp
public class CloudDetectionService
{
    public async Task<Result<CloudCoverageResult>> AnalyzeCloudsAsync(
        string fitsFilePath,
        int starCount)
    {
        try
        {
            using var fits = FitsFile.Open(fitsFilePath);
            var pixels = fits.ReadImageData<ushort>();
            var (width, height) = fits.GetImageDimensions();
            
            // Calculate expected star count for clear sky
            var expectedStars = GetExpectedStarCount(
                fits.GetKeywordValue<double>("EXPOSURE"),
                fits.GetKeywordValue<double>("CCD-TEMP"));
            
            // Cloud coverage based on star count deficit
            var cloudPercentage = CalculateCloudCoverage(
                detectedStars: starCount,
                expectedStars: expectedStars);
            
            // Pixel-level cloud detection (bright diffuse regions)
            var cloudMask = DetectCloudPixels(pixels, width, height);
            var pixelCloudPercentage = cloudMask.Count(c => c) / (double)(width * height) * 100;
            
            // Weighted average of both methods
            var finalCloudPercentage = (cloudPercentage * 0.6) + (pixelCloudPercentage * 0.4);
            
            _logger.LogInformation(
                "Cloud coverage: {CloudPercent:F1}% (Stars: {Detected}/{Expected})",
                finalCloudPercentage,
                starCount,
                expectedStars);
            
            return Result<CloudCoverageResult>.Success(new CloudCoverageResult
            {
                CloudPercentage = finalCloudPercentage,
                StarBasedPercentage = cloudPercentage,
                PixelBasedPercentage = pixelCloudPercentage,
                SkyCondition = ClassifySkyCondition(finalCloudPercentage)
            });
        }
        catch (Exception ex)
        {
            return Result<CloudCoverageResult>.Failure(ex);
        }
    }
    
    private SkyCondition ClassifySkyCondition(double cloudPercentage)
    {
        return cloudPercentage switch
        {
            < 10 => SkyCondition.Clear,
            < 30 => SkyCondition.MostlyClear,
            < 60 => SkyCondition.PartlyCloudy,
            < 90 => SkyCondition.MostlyCloudy,
            _ => SkyCondition.Overcast
        };
    }
}
```

### Real-Time Blazor Dashboard

```razor
@page "/sky-monitor"
@inject SkyMonitorService SkyMonitor
@implements IDisposable

<div class="sky-monitor-dashboard">
    <div class="sky-image-container">
        @if (!string.IsNullOrEmpty(_latestImagePath))
        {
            <img src="@GetImageUrl(_latestImagePath)" 
                 alt="All-Sky View" 
                 class="sky-image" />
        }
        
        <div class="sky-overlay">
            <div class="sky-stats">
                <span class="stat">
                    <i class="bi bi-star-fill"></i>
                    @_starCount stars
                </span>
                <span class="stat">
                    <i class="bi bi-cloud-fill"></i>
                    @_cloudPercentage.ToString("F1")% clouds
                </span>
                <span class="stat @GetSkyConditionClass()">
                    @_skyCondition
                </span>
            </div>
        </div>
    </div>
    
    <div class="monitoring-controls">
        <button class="btn btn-primary" 
                @onclick="StartMonitoring" 
                disabled="@_isMonitoring">
            Start Monitoring
        </button>
        
        <button class="btn btn-secondary" 
                @onclick="StopMonitoring" 
                disabled="@(!_isMonitoring)">
            Stop Monitoring
        </button>
        
        <button class="btn btn-success" @onclick="CaptureNow">
            Capture Now
        </button>
    </div>
    
    <div class="camera-settings">
        <label>
            Exposure:
            <input type="number" 
                   @bind="_exposureSeconds" 
                   min="1" 
                   max="300" 
                   step="1" /> seconds
        </label>
        
        <label>
            Gain:
            <input type="number" 
                   @bind="_gain" 
                   min="0" 
                   max="400" 
                   step="10" />
        </label>
    </div>
</div>

@code {
    private bool _isMonitoring;
    private int _starCount;
    private double _cloudPercentage;
    private SkyCondition _skyCondition;
    private string? _latestImagePath;
    private double _exposureSeconds = 30;
    private int _gain = 200;
    private Timer? _updateTimer;
    
    protected override void OnInitialized()
    {
        SkyMonitor.ImageCaptured += OnImageCaptured;
        _updateTimer = new Timer(
            _ => InvokeAsync(UpdateStats), 
            null, 
            TimeSpan.Zero, 
            TimeSpan.FromSeconds(2));
    }
    
    private async void OnImageCaptured(object? sender, ImageCapturedEventArgs e)
    {
        _latestImagePath = e.ImagePath;
        _starCount = e.StarCount;
        _cloudPercentage = e.CloudPercentage;
        _skyCondition = e.SkyCondition;
        
        await InvokeAsync(StateHasChanged);
    }
    
    private async Task StartMonitoring()
    {
        var config = new MonitoringConfig
        {
            ExposureSeconds = _exposureSeconds,
            Gain = _gain,
            IntervalSeconds = 60
        };
        
        await SkyMonitor.StartMonitoringAsync(config);
        _isMonitoring = true;
    }
    
    private async Task StopMonitoring()
    {
        await SkyMonitor.StopMonitoringAsync();
        _isMonitoring = false;
    }
    
    private async Task CaptureNow()
    {
        await SkyMonitor.CaptureImageAsync(_exposureSeconds, _gain);
    }
    
    private async Task UpdateStats()
    {
        var stats = await SkyMonitor.GetLatestStatsAsync();
        if (stats.IsSuccess)
        {
            _starCount = stats.Value.StarCount;
            _cloudPercentage = stats.Value.CloudPercentage;
            _skyCondition = stats.Value.SkyCondition;
            await InvokeAsync(StateHasChanged);
        }
    }
    
    private string GetSkyConditionClass()
    {
        return _skyCondition switch
        {
            SkyCondition.Clear => "text-success",
            SkyCondition.MostlyClear => "text-success",
            SkyCondition.PartlyCloudy => "text-warning",
            SkyCondition.MostlyCloudy => "text-warning",
            SkyCondition.Overcast => "text-danger",
            _ => "text-muted"
        };
    }
    
    public void Dispose()
    {
        SkyMonitor.ImageCaptured -= OnImageCaptured;
        _updateTimer?.Dispose();
    }
}
```

## 🎓 Usage Examples

### Automated Monitoring Service

```csharp
public class SkyMonitorBackgroundService : BackgroundService
{
    private readonly SkyMonitorService _skyMonitor;
    private readonly WeatherService _weather;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Capture all-sky image
                var captureResult = await _skyMonitor.CaptureImageAsync(
                    exposureSeconds: 30.0,
                    gain: 200);
                
                if (!captureResult.IsSuccess)
                {
                    _logger.LogError("Capture failed: {Error}", captureResult.Error.Message);
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }
                
                // Detect stars
                var starResult = await _skyMonitor.DetectStarsAsync(
                    captureResult.Value.FitsFilePath,
                    snrThreshold: 3.0);
                
                // Analyze clouds
                var cloudResult = await _skyMonitor.AnalyzeCloudsAsync(
                    captureResult.Value.FitsFilePath,
                    starResult.IsSuccess ? starResult.Value.StarCount : 0);
                
                // Store results
                if (starResult.IsSuccess && cloudResult.IsSuccess)
                {
                    await _skyMonitor.SaveAnalysisAsync(new SkyAnalysis
                    {
                        Timestamp = DateTime.UtcNow,
                        FitsFilePath = captureResult.Value.FitsFilePath,
                        StarCount = starResult.Value.StarCount,
                        CloudPercentage = cloudResult.Value.CloudPercentage,
                        SkyCondition = cloudResult.Value.SkyCondition
                    });
                }
                
                // Wait for next capture (default: 1 minute)
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sky monitoring error");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
```

### Historical Query API

```csharp
[ApiController]
[Route("api/v1/sky-history")]
public class SkyHistoryController : ControllerBase
{
    private readonly SkyMonitorDbContext _context;
    
    [HttpGet("tonight")]
    public async Task<IActionResult> GetTonightData()
    {
        var sunset = DateTime.Today.AddHours(18); // Approximate
        var now = DateTime.UtcNow;
        
        var data = await _context.SkyAnalyses
            .Where(a => a.Timestamp >= sunset && a.Timestamp <= now)
            .OrderBy(a => a.Timestamp)
            .Select(a => new
            {
                a.Timestamp,
                a.StarCount,
                a.CloudPercentage,
                SkyCondition = a.SkyCondition.ToString()
            })
            .ToListAsync();
        
        return Ok(data);
    }
    
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end)
    {
        var stats = await _context.SkyAnalyses
            .Where(a => a.Timestamp >= start && a.Timestamp <= end)
            .GroupBy(a => 1)
            .Select(g => new
            {
                TotalImages = g.Count(),
                AverageStarCount = g.Average(a => a.StarCount),
                AverageCloudCoverage = g.Average(a => a.CloudPercentage),
                ClearNights = g.Count(a => a.CloudPercentage < 10),
                CloudyNights = g.Count(a => a.CloudPercentage > 60)
            })
            .FirstOrDefaultAsync();
        
        return Ok(stats);
    }
}
```

## 🧪 Testing

### Run Domain Tests
```bash
cd src/HVO.SkyMonitorV5
dotnet test
```

### Run Benchmarks
```bash
cd src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Benchmarks
dotnet run -c Release
```

### Run Stress Tests
```bash
cd src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Stress
dotnet run -c Release -- --duration 24h
```

### Test Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

## 🐳 Docker Deployment

### Build Container
```bash
cd src/HVO.SkyMonitorV5
docker build -t hvo-skymonitor-v5:latest -f HVO.SkyMonitorV5.RPi/Dockerfile .
```

### Run on Raspberry Pi 5
```bash
docker run -d \
  --name skymonitor-v5 \
  --privileged \
  -p 5001:8080 \
  -v /data/sky-images:/app/data \
  -v /dev/bus/usb:/dev/bus/usb \
  hvo-skymonitor-v5:latest
```

### Docker Compose
```bash
docker-compose up -d
```

## ⚙️ Configuration

### appsettings.json
```json
{
  "SkyMonitor": {
    "Camera": {
      "Model": "ASI183MM Pro",
      "DefaultExposure": 30.0,
      "DefaultGain": 200,
      "DefaultOffset": 50,
      "CoolingEnabled": true,
      "TargetTemperature": -10.0
    },
    "Processing": {
      "StarDetectionSNR": 3.0,
      "MinStarFWHM": 1.5,
      "MaxStarFWHM": 8.0,
      "ParallelProcessing": true
    },
    "Storage": {
      "FitsArchivePath": "/data/sky-images",
      "RetentionDays": 90,
      "CompressOldImages": true
    },
    "Monitoring": {
      "IntervalSeconds": 60,
      "AutoStartOnBoot": true
    }
  }
}
```

## 📊 Performance Benchmarks

### Raspberry Pi 5 (4GB RAM)
- **Image Capture**: ~30s (exposure time)
- **Star Detection**: ~2.5s (1920×1080 image)
- **Cloud Analysis**: ~1.2s
- **Total Processing**: ~34s per image
- **Memory Usage**: ~180MB peak

### M2 Mac (Development)
- **Image Capture**: N/A (simulated)
- **Star Detection**: ~0.8s
- **Cloud Analysis**: ~0.4s
- **Total Processing**: ~1.2s

See detailed benchmarks: [Performance Benchmarks](../../docs/performance-benchmarks.md)

## 🔗 Dependencies

- `HVO.ZWOOptical.ASISDK` - ZWO camera control
- `HVO.Astronomy.CFITSIO` - FITS file I/O
- `HVO` - Core library (Result<T>, Option<T>)
- `SkiaSharp` - Image processing
- `HVO.WebSite.Themes` - Blazor UI theme

## 📚 Used By

- Observatory operators for sky conditions monitoring
- Automated imaging decision system (clear sky → start imaging)
- Weather correlation research
- Long-term sky quality assessment

## 🎨 Design Decisions

### Why SkiaSharp Over ImageSharp?
- **Performance**: 2-3x faster on Raspberry Pi (hardware acceleration)
- **Cross-platform**: Consistent behavior across ARM64/x64
- **Native libraries**: Better integration with System.Drawing concepts
- **Memory efficiency**: Lower allocations for high-frequency processing

### Why FITS Over JPEG/PNG?
- **Preservation**: Lossless 16-bit pixel data
- **Metadata**: Extensive header keywords (exposure, temperature, etc.)
- **Scientific standard**: Compatible with astronomical software
- **Archival**: Long-term data integrity

## 🔄 Future Enhancements

- [ ] Add constellation overlay on sky images
- [ ] Implement meteor detection
- [ ] Add satellite tracking and avoidance
- [ ] Create time-lapse video generation
- [ ] Add light pollution analysis
- [ ] Implement aurora detection for northern latitudes
- [ ] Add mobile push notifications for clear skies
- [ ] Create machine learning cloud classifier

## 📖 Related Documentation

- [SkyMonitor V5 Architecture](../../docs/sky-monitor-starfield.md)
- [SkyMonitor V5 Operations Runbook](../../docs/skymonitor-v5-operations-runbook.md)
- [SkyMonitor V5 Docker Deployment](../../docs/skymonitor-v5-docker.md)
- [Performance Benchmarks](../../docs/performance-benchmarks.md)
- [Migration from V4](../../docs/skymonitor-v5-json-migration-guide.md)
