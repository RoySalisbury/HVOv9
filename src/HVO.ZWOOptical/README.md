# HVO.ZWOOptical - ZWO ASI Camera SDK Integration

[![ZWO Optical Domain CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/zwooptical.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/zwooptical.yml)

Domain providing .NET integration with **ZWO (ZhongYi Optical) ASI cameras**, enabling astronomical imaging with industry-leading CMOS sensors for astrophotography and all-sky monitoring.

## 📦 Domain Overview

The **HVO.ZWOOptical** domain enables:
- **ASI camera control** - Connect, configure, and capture images from ZWO ASI cameras
- **Cross-platform support** - Linux ARM64 (Raspberry Pi), macOS, Windows
- **Native SDK wrapper** - P/Invoke bindings to ZWO's ASI SDK
- **Camera parameter management** - Exposure, gain, offset, cooling, binning
- **High-bit-depth imaging** - 16-bit RAW captures for astrophotography
- **USB 3.0 optimization** - High-speed image downloads

## 📁 Projects in This Domain

### HVO.ZWOOptical.ASISDK
.NET wrapper around the ZWO ASI SDK:
- P/Invoke bindings to ASI SDK functions
- Type-safe C# APIs for camera operations
- Camera discovery and enumeration
- Exposure control and image capture
- Camera cooling and temperature monitoring
- ROI (Region of Interest) configuration
- Native library deployment (platform-specific)

## 🔑 Key Features

### Camera Discovery and Connection

```csharp
using HVO.ZWOOptical.ASISDK;

// Enumerate connected cameras
var cameras = ASICameraSDK.GetConnectedCameras();
Console.WriteLine($"Found {cameras.Count} ZWO cameras");

foreach (var info in cameras)
{
    Console.WriteLine($"  {info.Name} (ID: {info.CameraId})");
    Console.WriteLine($"    Sensor: {info.MaxWidth}×{info.MaxHeight}");
    Console.WriteLine($"    Pixel Size: {info.PixelSize:F2}μm");
    Console.WriteLine($"    Color: {(info.IsColorCamera ? "Yes" : "No")}");
}

// Open first camera
if (cameras.Count > 0)
{
    using var camera = new ASICamera(cameras[0].CameraId);
    Console.WriteLine($"Opened {camera.CameraInfo.Name}");
}
```

### Image Capture

```csharp
public class AstroCameraService
{
    private readonly ASICamera _camera;
    
    public async Task<Result<string>> CaptureImageAsync(
        double exposureSeconds,
        int gain,
        int offset,
        string outputPath)
    {
        try
        {
            // Configure camera
            _camera.SetControlValue(ASIControlType.Exposure, (int)(exposureSeconds * 1_000_000)); // microseconds
            _camera.SetControlValue(ASIControlType.Gain, gain);
            _camera.SetControlValue(ASIControlType.Offset, offset);
            
            // Set image format (16-bit RAW for astrophotography)
            _camera.SetImageFormat(ASIImageType.RAW16);
            
            // Start exposure
            _camera.StartExposure(isDark: false);
            
            // Wait for exposure to complete
            var status = ASIExposureStatus.Working;
            while (status == ASIExposureStatus.Working)
            {
                await Task.Delay(100);
                status = _camera.GetExposureStatus();
            }
            
            if (status != ASIExposureStatus.Success)
                return Result<string>.Failure(new Exception($"Exposure failed: {status}"));
            
            // Download image data
            var imageData = _camera.GetImageData();
            
            // Convert to FITS and save
            var fitsPath = ConvertToFits(imageData, _camera.CameraInfo, outputPath);
            
            return Result<string>.Success(fitsPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(ex);
        }
    }
    
    private string ConvertToFits(
        byte[] imageData, 
        ASICameraInfo cameraInfo, 
        string outputPath)
    {
        var width = cameraInfo.MaxWidth;
        var height = cameraInfo.MaxHeight;
        
        // Convert byte array to ushort array
        var pixels = new ushort[width * height];
        Buffer.BlockCopy(imageData, 0, pixels, 0, imageData.Length);
        
        // Create FITS file
        using var fits = FitsFile.Create(outputPath, width, height);
        fits.WriteImageData(pixels);
        
        // Add camera metadata
        fits.SetKeywordValue("INSTRUME", cameraInfo.Name);
        fits.SetKeywordValue("XPIXSZ", cameraInfo.PixelSize);
        fits.SetKeywordValue("YPIXSZ", cameraInfo.PixelSize);
        fits.SetKeywordValue("XBINNING", 1);
        fits.SetKeywordValue("YBINNING", 1);
        
        return outputPath;
    }
}
```

### Camera Cooling Control

```csharp
public class CameraCoolingService
{
    private readonly ASICamera _camera;
    private readonly ILogger<CameraCoolingService> _logger;
    
    public async Task<Result> CoolToTemperatureAsync(
        double targetTempCelsius,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if camera supports cooling
            if (!_camera.HasCooler)
                return Result.Failure(new InvalidOperationException("Camera does not have cooler"));
            
            // Enable cooler
            _camera.SetControlValue(ASIControlType.CoolerOn, 1);
            
            // Set target temperature
            _camera.SetControlValue(ASIControlType.TargetTemp, (int)targetTempCelsius);
            
            _logger.LogInformation(
                "Cooling camera to {TargetTemp}°C", 
                targetTempCelsius);
            
            // Wait for temperature to stabilize (within 1°C)
            while (!cancellationToken.IsCancellationRequested)
            {
                var currentTemp = _camera.GetControlValue(ASIControlType.Temperature) / 10.0;
                var coolerPower = _camera.GetControlValue(ASIControlType.CoolerPowerPerc);
                
                _logger.LogDebug(
                    "Current: {CurrentTemp:F1}°C, Target: {TargetTemp:F1}°C, Power: {Power}%",
                    currentTemp,
                    targetTempCelsius,
                    coolerPower);
                
                if (Math.Abs(currentTemp - targetTempCelsius) < 1.0)
                {
                    _logger.LogInformation(
                        "Temperature stabilized at {CurrentTemp:F1}°C", 
                        currentTemp);
                    return Result.Success();
                }
                
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            
            return Result.Failure(new OperationCanceledException("Cooling cancelled"));
        }
        catch (Exception ex)
        {
            return Result.Failure(ex);
        }
    }
    
    public void WarmUp()
    {
        _logger.LogInformation("Warming up camera");
        _camera.SetControlValue(ASIControlType.CoolerOn, 0);
    }
}
```

### Advanced Camera Configuration

```csharp
public class CameraConfigurationService
{
    private readonly ASICamera _camera;
    
    public void ConfigureForLongExposure()
    {
        // Minimize noise for long exposures
        _camera.SetControlValue(ASIControlType.HighSpeedMode, 0); // Low speed for lower noise
        _camera.SetControlValue(ASIControlType.HardwareBin, 1);   // No binning
        
        // Set USB bandwidth (0-100, lower = less interference)
        _camera.SetControlValue(ASIControlType.Bandwidthoverload, 40);
        
        // Disable auto-exposure/gain
        _camera.SetControlValue(ASIControlType.AutoExpMaxExpMS, 0);
        _camera.SetControlValue(ASIControlType.AutoExpMaxGain, 0);
    }
    
    public void ConfigureForAllSky()
    {
        // Optimize for fast all-sky monitoring
        _camera.SetControlValue(ASIControlType.HighSpeedMode, 1); // High speed mode
        _camera.SetControlValue(ASIControlType.HardwareBin, 2);   // 2×2 binning for speed
        
        // Higher USB bandwidth for faster downloads
        _camera.SetControlValue(ASIControlType.Bandwidthoverload, 80);
        
        // Set ROI to crop edges (faster download)
        var info = _camera.CameraInfo;
        _camera.SetROI(
            startX: info.MaxWidth / 8,
            startY: info.MaxHeight / 8,
            width: info.MaxWidth * 3 / 4,
            height: info.MaxHeight * 3 / 4,
            bin: 2);
    }
    
    public Dictionary<string, object> GetAllControlValues()
    {
        var controls = new Dictionary<string, object>();
        
        foreach (var controlType in Enum.GetValues<ASIControlType>())
        {
            try
            {
                var value = _camera.GetControlValue(controlType);
                controls[controlType.ToString()] = value;
            }
            catch
            {
                // Control not supported
            }
        }
        
        return controls;
    }
}
```

## 🎓 Usage Examples

### Background Service Integration

```csharp
public class CameraBackgroundService : BackgroundService
{
    private readonly ASICamera _camera;
    private readonly ILogger<CameraBackgroundService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Connect to camera
        var cameras = ASICameraSDK.GetConnectedCameras();
        if (cameras.Count == 0)
        {
            _logger.LogError("No ZWO cameras found");
            return;
        }
        
        using var camera = new ASICamera(cameras[0].CameraId);
        _logger.LogInformation("Connected to {CameraName}", camera.CameraInfo.Name);
        
        // Cool camera
        await CoolCameraAsync(camera, targetTemp: -10.0, stoppingToken);
        
        // Continuous imaging loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var imagePath = await CaptureImageAsync(camera);
                _logger.LogInformation("Captured image: {Path}", imagePath);
                
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Capture failed");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        
        // Warm up camera before shutdown
        WarmUpCamera(camera);
    }
}
```

## 🧪 Testing

### Run Domain Tests
```bash
cd src/HVO.ZWOOptical
dotnet test
```

### Build Domain Solution
```bash
cd src/HVO.ZWOOptical
dotnet build HVO.ZWOOptical.sln
```

## ⚙️ Configuration

### Supported Camera Models

The SDK supports all ZWO ASI cameras including:
- **ASI183MM/MC Pro** - 20MP Sony IMX183 (used in HVO)
- **ASI224MC** - High-speed planetary camera
- **ASI294MM/MC Pro** - 11.7MP APS-C sensor
- **ASI533MM/MC Pro** - 9MP BSI sensor
- **ASI2600MM/MC Pro** - 26MP full-frame
- **ASI6200MM/MC Pro** - 62MP full-frame

### appsettings.json
```json
{
  "ZWOCamera": {
    "PreferredCameraId": 0,
    "DefaultExposure": 30.0,
    "DefaultGain": 200,
    "DefaultOffset": 50,
    "EnableCooling": true,
    "TargetTemperature": -10.0,
    "USBBandwidth": 40,
    "HighSpeedMode": false
  }
}
```

## 🔗 Native Library Deployment

### Platform-Specific Libraries

```
HVO.ZWOOptical.ASISDK/
└── runtimes/
    ├── linux-arm64/native/libASICamera2.so
    ├── linux-x64/native/libASICamera2.so
    ├── osx-arm64/native/libASICamera2.dylib
    ├── osx-x64/native/libASICamera2.dylib
    └── win-x64/native/ASICamera2.dll
```

Libraries are automatically deployed via NuGet package structure.

## 📦 Dependencies

- ZWO ASI SDK (native libraries included)
- `HVO` - Core library (Result<T>, Option<T>)
- `Microsoft.Extensions.Logging.Abstractions` - Structured logging

## 📚 Used By

- `HVO.SkyMonitorV5.RPi` - All-sky camera imaging
- `HVO.SkyMonitorV4.RPi` - Legacy all-sky system
- Future: Deep-sky imaging automation

## 🎨 Design Considerations

### P/Invoke vs. Managed Library
- **Performance**: Native SDK provides best performance
- **Feature parity**: Direct access to all SDK features
- **Updates**: Easy to sync with ZWO SDK releases
- **Cross-platform**: Native libs for all platforms

### Thread Safety
All camera operations are thread-safe with internal locking:
```csharp
private readonly object _lock = new();

public void SetControlValue(ASIControlType type, int value)
{
    lock (_lock)
    {
        ASICameraSDK.ASISetControlValue(_cameraId, type, value, ASIBool.False);
    }
}
```

### Resource Management
Cameras properly dispose USB resources:
```csharp
public void Dispose()
{
    if (_isOpen)
    {
        ASICameraSDK.ASICloseCamera(_cameraId);
        _isOpen = false;
    }
}
```

## 🔄 Future Enhancements

- [ ] Add video mode support (ASI120/224 high-speed cameras)
- [ ] Implement auto-guiding camera support
- [ ] Add filter wheel control (ZWO EFW)
- [ ] Create camera simulator for testing
- [ ] Add ASCOM driver wrapper
- [ ] Implement dark/flat frame library management
- [ ] Add plate solving integration
- [ ] Create camera control UI component

## 📖 Related Documentation

- [ZWO Official Website](https://astronomy-imaging-camera.com/)
- [ASI SDK Documentation](https://astronomy-imaging-camera.com/software-drivers)
- [HVO SkyMonitor V5 (uses ASISDK)](../HVO.SkyMonitorV5/README.md)
- [Camera Comparison Guide](https://astronomy-imaging-camera.com/camera-comparison)
