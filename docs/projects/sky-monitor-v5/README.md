# Sky Monitor V5 Architecture

This document outlines the architecture and design of the Sky Monitor V5 system for automated sky condition monitoring and cloud detection.

## System Overview

Sky Monitor V5 is a comprehensive sky monitoring solution that captures images of the night sky, analyzes cloud coverage, detects stars, and provides real-time assessments for astronomical imaging conditions.

## Architecture Components

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   ZWO Camera    │    │   Image         │    │   FITS          │
│   Integration   │    │   Processing    │    │   Export        │
└─────────┬───────┘    └─────────┬───────┘    └─────────┬───────┘
          │                      │                      │
          └──────────────────────┼──────────────────────┘
                                 │
                    ┌─────────────┴─────────────┐
                    │   Sky Monitor V5         │
                    │   Core Service           │
                    └─────────────┬─────────────┘
                                  │
          ┌───────────────────────┼───────────────────────┐
          │                       │                       │
    ┌─────┴───────┐     ┌─────────┴─────────┐     ┌─────────┴─────────┐
    │   Star      │     │   Cloud           │     │   Web API         │
    │   Detection │     │   Detection       │     │   Interface       │
    └─────────────┘     └───────────────────┘     └───────────────────┘
```

## Core Services

### 1. Image Capture Service

Handles camera control and image acquisition:

```csharp
public class ImageCaptureService
{
    private readonly ZwoCamera _camera;
    private readonly ILogger<ImageCaptureService> _logger;
    
    public async Task<Result<CapturedImage>> CaptureImageAsync(CaptureSettings settings)
    {
        try
        {
            // Configure camera
            await _camera.SetExposureAsync(settings.ExposureTime);
            await _camera.SetGainAsync(settings.Gain);
            await _camera.SetBinningAsync(settings.Binning);
            
            // Capture image
            _logger.LogInformation("Starting sky capture - Exposure: {Exposure}s, Gain: {Gain}", 
                settings.ExposureTime, settings.Gain);
                
            var imageData = await _camera.CaptureImageAsync();
            
            // Create result
            var capturedImage = new CapturedImage
            {
                Data = imageData,
                Width = _camera.Width,
                Height = _camera.Height,
                Timestamp = DateTime.UtcNow,
                Settings = settings
            };
            
            return Result<CapturedImage>.Success(capturedImage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture sky image");
            return Result<CapturedImage>.Failure(ex);
        }
    }
}
```

### 2. Star Detection Service

Analyzes captured images to detect and count stars:

```csharp
public class StarDetectionService
{
    private readonly ILogger<StarDetectionService> _logger;
    
    public Result<StarDetectionResult> DetectStars(CapturedImage image, StarDetectionSettings settings)
    {
        try
        {
            var pixels = image.Data;
            var stars = new List<DetectedStar>();
            
            // Calculate noise threshold
            var noiseLevel = CalculateNoiseLevel(pixels);
            var threshold = noiseLevel * settings.SignalToNoiseRatio;
            
            _logger.LogDebug("Star detection - Noise: {Noise}, Threshold: {Threshold}, SNR: {SNR}", 
                noiseLevel, threshold, settings.SignalToNoiseRatio);
            
            // Find star candidates
            var candidates = FindStarCandidates(pixels, image.Width, image.Height, threshold);
            
            // Filter and validate stars
            foreach (var candidate in candidates)
            {
                if (ValidateStarCandidate(candidate, pixels, image.Width, image.Height))
                {
                    stars.Add(candidate);
                }
            }
            
            var result = new StarDetectionResult
            {
                Stars = stars,
                StarCount = stars.Count,
                NoiseLevel = noiseLevel,
                Threshold = threshold
            };
            
            _logger.LogInformation("Detected {StarCount} stars (SNR: {SNR})", 
                stars.Count, settings.SignalToNoiseRatio);
                
            return Result<StarDetectionResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Star detection failed");
            return Result<StarDetectionResult>.Failure(ex);
        }
    }
    
    private double CalculateNoiseLevel(ushort[] pixels)
    {
        // Use robust noise estimation (median absolute deviation)
        var sorted = pixels.OrderBy(p => p).ToArray();
        var median = sorted[sorted.Length / 2];
        
        var deviations = pixels.Select(p => Math.Abs(p - median)).OrderBy(d => d).ToArray();
        var mad = deviations[deviations.Length / 2];
        
        return mad * 1.4826; // Scale factor for normal distribution
    }
}
```

### 3. Cloud Detection Service

Analyzes sky brightness and patterns to detect cloud coverage:

```csharp
public class CloudDetectionService
{
    public Result<CloudDetectionResult> DetectClouds(CapturedImage image, List<DetectedStar> stars)
    {
        try
        {
            // Calculate sky brightness statistics
            var skyBrightness = CalculateSkyBrightness(image.Data);
            
            // Analyze spatial brightness variations
            var cloudPatterns = AnalyzeCloudPatterns(image);
            
            // Estimate cloud coverage based on star count and brightness
            var cloudCoverage = EstimateCloudCoverage(stars.Count, skyBrightness, cloudPatterns);
            
            var result = new CloudDetectionResult
            {
                CloudCoveragePercentage = cloudCoverage,
                SkyBrightness = skyBrightness,
                StarCount = stars.Count,
                Quality = DetermineImageQuality(cloudCoverage, stars.Count)
            };
            
            _logger.LogInformation("Cloud analysis complete - Coverage: {Coverage}%, Stars: {StarCount}, Quality: {Quality}", 
                cloudCoverage, stars.Count, result.Quality);
                
            return Result<CloudDetectionResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud detection failed");
            return Result<CloudDetectionResult>.Failure(ex);
        }
    }
    
    private double EstimateCloudCoverage(int starCount, double skyBrightness, CloudPatternAnalysis patterns)
    {
        // Multi-factor cloud coverage estimation
        var starFactor = Math.Max(0, (100 - starCount) / 100.0 * 100);
        var brightnessFactor = Math.Min(100, (skyBrightness - 1000) / 10);
        var patternFactor = patterns.CloudIndicatorStrength * 100;
        
        // Weighted average of factors
        var coverage = (starFactor * 0.4 + brightnessFactor * 0.3 + patternFactor * 0.3);
        return Math.Max(0, Math.Min(100, coverage));
    }
}
```

### 4. FITS Export Service

Exports captured images and metadata to FITS format:

```csharp
public class FitsExportService
{
    public async Task<Result<string>> ExportToFitsAsync(CapturedImage image, SkyAnalysisResult analysis)
    {
        try
        {
            var filename = GenerateFitsFilename(image.Timestamp);
            var fitsPath = Path.Combine(_settings.OutputDirectory, filename);
            
            using var fits = FitsFile.Create(fitsPath, image.Width, image.Height);
            
            // Write image data
            fits.WriteImageData(image.Data);
            
            // Add standard headers
            fits.SetKeywordValue("TELESCOP", "Hualapai Valley Observatory");
            fits.SetKeywordValue("INSTRUME", "ZWO ASI183MM Pro");
            fits.SetKeywordValue("OBSERVER", "Sky Monitor V5");
            fits.SetKeywordValue("OBJECT", "Sky Survey");
            
            // Add capture settings
            fits.SetKeywordValue("EXPTIME", image.Settings.ExposureTime);
            fits.SetKeywordValue("GAIN", image.Settings.Gain);
            fits.SetKeywordValue("BINNING", image.Settings.Binning);
            fits.SetKeywordValue("DATE-OBS", image.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff"));
            
            // Add analysis results
            fits.SetKeywordValue("STARCOUNT", analysis.StarCount);
            fits.SetKeywordValue("CLOUDCVR", analysis.CloudCoverage);
            fits.SetKeywordValue("SKYBRGHT", analysis.SkyBrightness);
            fits.SetKeywordValue("IMGQUAL", analysis.Quality.ToString());
            
            _logger.LogInformation("FITS export complete: {Filename}", filename);
            return Result<string>.Success(fitsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FITS export failed");
            return Result<string>.Failure(ex);
        }
    }
}
```

## Data Models

### Captured Image

```csharp
public class CapturedImage
{
    public ushort[] Data { get; set; } = Array.Empty<ushort>();
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime Timestamp { get; set; }
    public CaptureSettings Settings { get; set; } = new();
}

public class CaptureSettings
{
    public double ExposureTime { get; set; } = 10.0;
    public int Gain { get; set; } = 200;
    public int Binning { get; set; } = 1;
    public bool AutoExposure { get; set; } = false;
}
```

### Analysis Results

```csharp
public class SkyAnalysisResult
{
    public int StarCount { get; set; }
    public double CloudCoverage { get; set; }
    public double SkyBrightness { get; set; }
    public ImageQuality Quality { get; set; }
    public DateTime Timestamp { get; set; }
    public List<DetectedStar> Stars { get; set; } = new();
}

public class DetectedStar
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Brightness { get; set; }
    public double SignalToNoise { get; set; }
}

public enum ImageQuality
{
    Excellent,  // <10% clouds, >50 stars
    Good,       // 10-30% clouds, 25-50 stars  
    Fair,       // 30-60% clouds, 10-25 stars
    Poor,       // 60-80% clouds, 5-10 stars
    Unusable    // >80% clouds, <5 stars
}
```

## Configuration

### Sky Monitor Settings

```json
{
  "SkyMonitor": {
    "Camera": {
      "ExposureTime": 10.0,
      "Gain": 200,
      "Binning": 1,
      "CoolingEnabled": true,
      "TargetTemperature": -10.0
    },
    "StarDetection": {
      "SignalToNoiseRatio": 3.0,
      "MinimumBrightness": 100,
      "MaximumStars": 500
    },
    "CloudDetection": {
      "BrightnessThreshold": 1500,
      "PatternAnalysisEnabled": true,
      "HistoricalComparisonEnabled": true
    },
    "Export": {
      "EnableFitsExport": true,
      "OutputDirectory": "/data/skymonitor",
      "RetentionDays": 30,
      "CompressionEnabled": true
    },
    "Automation": {
      "CaptureInterval": "00:02:00",
      "StartTime": "20:00:00",
      "EndTime": "06:00:00",
      "EnableScheduledCapture": true
    }
  }
}
```

## Performance Optimization

### Image Processing Pipeline

```csharp
public class ImageProcessingPipeline
{
    public async Task<Result<SkyAnalysisResult>> ProcessImageAsync(CapturedImage image)
    {
        // Process star detection and cloud analysis in parallel
        var starTask = Task.Run(() => _starDetection.DetectStars(image, _starSettings));
        var cloudTask = Task.Run(() => _cloudDetection.AnalyzeBrightness(image));
        
        await Task.WhenAll(starTask, cloudTask);
        
        var stars = starTask.Result;
        var brightness = cloudTask.Result;
        
        if (!stars.IsSuccess) return Result<SkyAnalysisResult>.Failure(stars.Error);
        if (!brightness.IsSuccess) return Result<SkyAnalysisResult>.Failure(brightness.Error);
        
        // Combine results
        var cloudResult = _cloudDetection.EstimateCoverage(stars.Value.Stars, brightness.Value);
        
        var analysis = new SkyAnalysisResult
        {
            StarCount = stars.Value.StarCount,
            CloudCoverage = cloudResult.Value.CloudCoveragePercentage,
            SkyBrightness = brightness.Value.MeanBrightness,
            Quality = DetermineQuality(stars.Value.StarCount, cloudResult.Value.CloudCoveragePercentage),
            Stars = stars.Value.Stars,
            Timestamp = image.Timestamp
        };
        
        return Result<SkyAnalysisResult>.Success(analysis);
    }
}
```

### Memory Management

```csharp
public class ImageBufferPool
{
    private readonly ConcurrentQueue<ushort[]> _buffers = new();
    private readonly int _bufferSize;
    
    public ImageBufferPool(int width, int height, int poolSize = 5)
    {
        _bufferSize = width * height;
        
        // Pre-allocate buffers
        for (int i = 0; i < poolSize; i++)
        {
            _buffers.Enqueue(new ushort[_bufferSize]);
        }
    }
    
    public ushort[] RentBuffer()
    {
        return _buffers.TryDequeue(out var buffer) ? buffer : new ushort[_bufferSize];
    }
    
    public void ReturnBuffer(ushort[] buffer)
    {
        if (buffer.Length == _bufferSize)
        {
            Array.Clear(buffer, 0, buffer.Length);
            _buffers.Enqueue(buffer);
        }
    }
}
```

## Testing Strategy

### Unit Tests

```csharp
[TestMethod]
public void StarDetection_WithSimulatedStars_DetectsCorrectCount()
{
    // Arrange
    var image = CreateImageWithSimulatedStars(starCount: 25);
    var settings = new StarDetectionSettings { SignalToNoiseRatio = 3.0 };
    
    // Act
    var result = _starDetection.DetectStars(image, settings);
    
    // Assert
    Assert.IsTrue(result.IsSuccess);
    Assert.AreEqual(25, result.Value.StarCount, delta: 2); // Allow small variance
}
```

### Integration Tests

```csharp
[TestMethod]
public async Task SkyMonitor_EndToEndCapture_ProducesValidAnalysis()
{
    // Arrange
    var captureSettings = new CaptureSettings { ExposureTime = 5.0, Gain = 100 };
    
    // Act
    var captureResult = await _captureService.CaptureImageAsync(captureSettings);
    Assert.IsTrue(captureResult.IsSuccess);
    
    var analysisResult = await _processingPipeline.ProcessImageAsync(captureResult.Value);
    
    // Assert
    Assert.IsTrue(analysisResult.IsSuccess);
    Assert.IsTrue(analysisResult.Value.StarCount >= 0);
    Assert.IsTrue(analysisResult.Value.CloudCoverage >= 0 && analysisResult.Value.CloudCoverage <= 100);
}
```

## Monitoring and Metrics

### Performance Metrics

```csharp
public class SkyMonitorMetrics
{
    private readonly ILogger<SkyMonitorMetrics> _logger;
    private readonly Dictionary<string, TimeSpan> _operationTimes = new();
    
    public void RecordOperationTime(string operation, TimeSpan duration)
    {
        _operationTimes[operation] = duration;
        
        _logger.LogInformation("Performance: {Operation} completed in {Duration}ms", 
            operation, duration.TotalMilliseconds);
            
        // Alert on slow operations
        if (duration > TimeSpan.FromSeconds(30))
        {
            _logger.LogWarning("Slow operation detected: {Operation} took {Duration}ms", 
                operation, duration.TotalMilliseconds);
        }
    }
    
    public Dictionary<string, object> GetMetricsSummary()
    {
        return new Dictionary<string, object>
        {
            ["AverageCaptureTime"] = _operationTimes.GetValueOrDefault("Capture", TimeSpan.Zero).TotalSeconds,
            ["AverageProcessingTime"] = _operationTimes.GetValueOrDefault("Processing", TimeSpan.Zero).TotalSeconds,
            ["AverageExportTime"] = _operationTimes.GetValueOrDefault("Export", TimeSpan.Zero).TotalSeconds
        };
    }
}
```

## Error Handling and Recovery

### Camera Error Recovery

```csharp
public class CameraErrorRecovery
{
    public async Task<Result> RecoverFromCameraErrorAsync(Exception error)
    {
        _logger.LogError(error, "Camera error occurred, initiating recovery");
        
        try
        {
            // Attempt to reconnect camera
            await _camera.DisconnectAsync();
            await Task.Delay(TimeSpan.FromSeconds(5));
            await _camera.ConnectAsync();
            
            // Verify camera is functional
            var testCapture = await _camera.CaptureImageAsync(exposure: 1.0);
            if (testCapture != null)
            {
                _logger.LogInformation("Camera recovery successful");
                return Result.Success();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Camera recovery failed");
        }
        
        return Result.Failure(new Exception("Unable to recover camera connection"));
    }
}
```

## Future Enhancements

### Planned Features

- [ ] **Machine learning cloud detection** using trained models
- [ ] **Historical trend analysis** for weather pattern recognition
- [ ] **Real-time streaming** to web dashboard
- [ ] **Multi-camera support** for all-sky monitoring
- [ ] **Integration with weather station data**
- [ ] **Automated image calibration** (dark/flat field correction)

### Advanced Analysis

- [ ] **Seeing measurement** using star profile analysis
- [ ] **Sky transparency estimation** using photometry
- [ ] **Light pollution monitoring** with trend analysis
- [ ] **Satellite trail detection** and masking
- [ ] **Meteor detection** and automated recording

## Related Documentation

- [ZWO Camera Integration](../zwo-optical/README.md)
- [FITS Export Implementation](../astronomy/fits-export.md)
- [Observatory Automation](../../observatory-automation.md)
- [Sky Monitor V5 Operations Runbook](../../skymonitor-v5-operations-runbook.md)