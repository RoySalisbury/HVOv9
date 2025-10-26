# HVO.Playground - Development Utilities and Experiments

[![Playground CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/playground.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/playground.yml)

Domain containing experimental utilities, testing tools, and development sandbox projects for prototyping features before integration into production systems.

## 📦 Domain Overview

The **HVO.Playground** domain provides:
- **Rapid prototyping** - Test new ideas without affecting production code
- **Hardware testing** - GPIO device verification and troubleshooting
- **CLI utilities** - Command-line tools for data processing and analysis
- **API experimentation** - Test external service integrations
- **Performance testing** - Benchmark alternative implementations
- **Learning sandbox** - Safe environment for trying new .NET features

## 📁 Projects in This Domain

### HVO.Playground.CLI
General-purpose command-line utility:
- FITS file analysis and manipulation
- Image processing experiments
- Data export/import tools
- API testing and debugging
- Configuration file generators
- Batch processing scripts

### HVO.GpioTestApp
Hardware device testing utility:
- GPIO pin verification (Raspberry Pi)
- Limit switch testing and calibration
- Relay operation validation
- Wiring troubleshooting
- Hardware simulation testing
- Interactive device control

## 🔑 Key Features

### FITS File Analysis CLI

```csharp
// HVO.Playground.CLI/Commands/FitsAnalyzeCommand.cs
public class FitsAnalyzeCommand
{
    public async Task<int> ExecuteAsync(string fitsPath)
    {
        try
        {
            Console.WriteLine($"Analyzing FITS file: {fitsPath}");
            
            using var fits = FitsFile.Open(fitsPath);
            var (width, height) = fits.GetImageDimensions();
            
            Console.WriteLine($"Dimensions: {width}×{height}");
            
            // Read and analyze pixel data
            var pixels = fits.ReadImageData<ushort>();
            var min = pixels.Min();
            var max = pixels.Max();
            var mean = pixels.Average(p => (double)p);
            var median = CalculateMedian(pixels);
            
            Console.WriteLine($"Pixel Statistics:");
            Console.WriteLine($"  Min:    {min}");
            Console.WriteLine($"  Max:    {max}");
            Console.WriteLine($"  Mean:   {mean:F2}");
            Console.WriteLine($"  Median: {median:F2}");
            
            // Display header keywords
            Console.WriteLine("\nHeader Keywords:");
            var headers = fits.GetAllHeaders();
            foreach (var (keyword, value, comment) in headers.Take(20))
            {
                Console.WriteLine($"  {keyword,-8} = {value,-20} / {comment}");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
    
    private double CalculateMedian(ushort[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
```

### GPIO Device Testing

```csharp
// HVO.GpioTestApp/Program.cs
public class GpioTestApp
{
    private static GpioController? _controller;
    
    public static async Task Main(string[] args)
    {
        Console.WriteLine("HVO GPIO Test Utility");
        Console.WriteLine("=====================\n");
        
        // Initialize GPIO controller
        _controller = new GpioController();
        
        while (true)
        {
            Console.WriteLine("\nCommands:");
            Console.WriteLine("  1. Test limit switch");
            Console.WriteLine("  2. Test relay");
            Console.WriteLine("  3. Monitor all pins");
            Console.WriteLine("  4. Exit");
            Console.Write("\nSelect option: ");
            
            var choice = Console.ReadLine();
            
            switch (choice)
            {
                case "1":
                    await TestLimitSwitchAsync();
                    break;
                case "2":
                    await TestRelayAsync();
                    break;
                case "3":
                    await MonitorPinsAsync();
                    break;
                case "4":
                    return;
            }
        }
    }
    
    private static async Task TestLimitSwitchAsync()
    {
        Console.Write("Enter pin number: ");
        var pinNumber = int.Parse(Console.ReadLine()!);
        
        Console.Write("Polarity (NO/NC): ");
        var polarity = Console.ReadLine()!.ToUpper() == "NO"
            ? GpioLimitSwitch.SwitchPolarity.NormallyOpen
            : GpioLimitSwitch.SwitchPolarity.NormallyClosed;
        
        using var limitSwitch = new GpioLimitSwitch(
            pinNumber, 
            polarity, 
            _controller!);
        
        limitSwitch.StateChanged += (sender, e) =>
        {
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] Switch {(e.IsClosed ? "CLOSED" : "OPEN")}");
        };
        
        Console.WriteLine("\nMonitoring limit switch (press any key to stop)...");
        Console.WriteLine($"Initial state: {(limitSwitch.IsClosed ? "CLOSED" : "OPEN")}");
        
        await Task.Run(() => Console.ReadKey(true));
    }
    
    private static async Task TestRelayAsync()
    {
        Console.Write("Enter pin number: ");
        var pinNumber = int.Parse(Console.ReadLine()!);
        
        using var relay = new GpioRelay(pinNumber, _controller!);
        
        Console.WriteLine("\nRelay Test Sequence:");
        
        for (int i = 0; i < 5; i++)
        {
            Console.WriteLine($"  Cycle {i + 1}/5");
            
            Console.WriteLine("    ON");
            relay.SetState(true);
            await Task.Delay(1000);
            
            Console.WriteLine("    OFF");
            relay.SetState(false);
            await Task.Delay(1000);
        }
        
        Console.WriteLine("Relay test complete!");
    }
    
    private static async Task MonitorPinsAsync()
    {
        Console.Write("Enter pin numbers (comma-separated): ");
        var pins = Console.ReadLine()!
            .Split(',')
            .Select(p => int.Parse(p.Trim()))
            .ToArray();
        
        foreach (var pin in pins)
        {
            _controller!.OpenPin(pin, PinMode.Input);
        }
        
        Console.WriteLine("\nMonitoring pins (press any key to stop)...");
        
        var cts = new CancellationTokenSource();
        var monitorTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                Console.Write($"\r[{DateTime.Now:HH:mm:ss}] ");
                
                foreach (var pin in pins)
                {
                    var value = _controller!.Read(pin);
                    Console.Write($"Pin{pin}:{(value == PinValue.High ? "HIGH" : "LOW ")} ");
                }
                
                await Task.Delay(100, cts.Token);
            }
        }, cts.Token);
        
        Console.ReadKey(true);
        cts.Cancel();
        
        await monitorTask;
        Console.WriteLine("\n");
        
        foreach (var pin in pins)
        {
            _controller!.ClosePin(pin);
        }
    }
}
```

### Batch FITS Processing

```csharp
// HVO.Playground.CLI/Commands/BatchProcessCommand.cs
public class BatchProcessCommand
{
    public async Task<int> ExecuteAsync(string inputDir, string outputDir)
    {
        var fitsFiles = Directory.GetFiles(inputDir, "*.fits");
        
        Console.WriteLine($"Processing {fitsFiles.Length} FITS files...");
        
        var progress = 0;
        var results = new List<ProcessingResult>();
        
        foreach (var fitsFile in fitsFiles)
        {
            try
            {
                var result = await ProcessFitsFileAsync(fitsFile, outputDir);
                results.Add(result);
                
                progress++;
                Console.WriteLine($"[{progress}/{fitsFiles.Length}] {Path.GetFileName(fitsFile)} - {result.StarCount} stars");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing {fitsFile}: {ex.Message}");
            }
        }
        
        // Generate summary report
        GenerateSummaryReport(results, outputDir);
        
        return 0;
    }
    
    private async Task<ProcessingResult> ProcessFitsFileAsync(
        string fitsPath, 
        string outputDir)
    {
        using var fits = FitsFile.Open(fitsPath);
        var pixels = fits.ReadImageData<ushort>();
        
        // Detect stars
        var stars = DetectStars(pixels, fits.GetImageDimensions());
        
        // Save annotated image
        var outputPath = Path.Combine(outputDir, Path.GetFileName(fitsPath));
        SaveAnnotatedImage(pixels, stars, outputPath);
        
        return new ProcessingResult
        {
            FileName = Path.GetFileName(fitsPath),
            StarCount = stars.Count,
            ProcessedPath = outputPath
        };
    }
}
```

## 🎓 Usage Examples

### Running CLI Commands

```bash
# Analyze a FITS file
cd src/HVO.Playground/HVO.Playground.CLI
dotnet run -- analyze /data/M31_Light_300s_001.fits

# Batch process directory
dotnet run -- batch-process /data/raw /data/processed

# Export data to CSV
dotnet run -- export-csv /data/weather.db weather-2024.csv
```

### GPIO Testing Workflow

```bash
# Run GPIO test app on Raspberry Pi
cd src/HVO.Playground/HVO.GpioTestApp
dotnet run

# Follow interactive prompts:
# 1. Select "Test limit switch"
# 2. Enter pin number: 17
# 3. Enter polarity: NO
# 4. Trigger switch physically to verify detection
```

### Experimenting with New Features

```csharp
// HVO.Playground.CLI/Experiments/StarDetectionExperiment.cs
public class StarDetectionExperiment
{
    // Try different SNR thresholds to find optimal value
    public async Task CompareSnrThresholdsAsync(string fitsPath)
    {
        var thresholds = new[] { 2.0, 2.5, 3.0, 3.5, 4.0, 5.0 };
        
        Console.WriteLine("SNR Threshold Comparison:");
        Console.WriteLine("Threshold | Star Count | Processing Time");
        Console.WriteLine("----------|------------|----------------");
        
        foreach (var threshold in thresholds)
        {
            var sw = Stopwatch.StartNew();
            var result = await DetectStarsAsync(fitsPath, threshold);
            sw.Stop();
            
            Console.WriteLine($"{threshold,9:F1} | {result.StarCount,10} | {sw.ElapsedMilliseconds,12}ms");
        }
    }
}
```

## 🧪 Testing

### Run Playground Tests
```bash
cd src/HVO.Playground
dotnet test
```

### Build Playground Solution
```bash
cd src/HVO.Playground
dotnet build HVO.Playground.sln
```

### Run Specific Utility
```bash
cd src/HVO.Playground/HVO.Playground.CLI
dotnet run -- [command] [args]
```

## 📋 Common Playground Tasks

### 1. Hardware Troubleshooting
**Problem**: Limit switch not detecting state changes
```bash
dotnet run --project HVO.GpioTestApp
# Select "Monitor all pins"
# Enter pin numbers: 17,27
# Physically trigger switches and verify state changes
```

### 2. FITS File Investigation
**Problem**: Understanding image structure
```bash
dotnet run --project HVO.Playground.CLI -- analyze mystery.fits
# Review header keywords and pixel statistics
```

### 3. Performance Testing
**Problem**: Which star detection algorithm is faster?
```csharp
// Add experiment to HVO.Playground.CLI
[Benchmark]
public void DetectStars_Method1() { /* ... */ }

[Benchmark]
public void DetectStars_Method2() { /* ... */ }
```

### 4. Data Export
**Problem**: Need to analyze data in Excel/Python
```bash
dotnet run --project HVO.Playground.CLI -- export-csv weather.db output.csv
# Import CSV into analysis tools
```

## 🔗 Dependencies

- `HVO` - Core library
- `HVO.Astronomy.CFITSIO` - FITS file access
- `HVO.Iot.Devices` - Hardware device testing
- `HVO.DataModels` - Database access (for exports)
- `System.CommandLine` - CLI parsing (optional)
- `BenchmarkDotNet` - Performance testing (optional)

## 📚 When to Use Playground

### ✅ Good Use Cases
- Testing new hardware devices before integration
- Prototyping image processing algorithms
- Experimenting with API integrations
- Creating one-off data migration scripts
- Benchmarking alternative implementations
- Learning new .NET features

### ❌ Not For
- Production automation (use proper projects)
- Shared utilities (move to HVO core library)
- Web APIs (use HVO.WebSite.Playground)
- Permanent tools (create dedicated project)

## 🎨 Design Philosophy

### Rapid Experimentation
- **No strict architecture** - Move fast, iterate quickly
- **Throwaway code** - It's okay to delete experiments
- **Copy-paste friendly** - Duplicate code to test variations
- **Minimal dependencies** - Avoid coupling to production systems

### Graduation Path
When playground code proves valuable:
1. **Refactor** - Clean up, add error handling, tests
2. **Move to proper project** - Create dedicated project in appropriate domain
3. **Document** - Add comprehensive documentation
4. **Integrate** - Wire into production systems

## 🔄 Current Experiments

### Active
- Star detection SNR threshold optimization
- GPIO debounce timing calibration
- FITS file compression comparison
- Alternative cloud detection algorithms

### Graduated to Production
- GpioLimitSwitch (→ HVO.Iot.Devices)
- HistoryLineChart component (→ HVO.WebSite.Themes)
- ProcessedFrameEncoder (→ HVO.SkyMonitorV5.RPi)

### Archived
- ImageSharp vs SkiaSharp comparison (SkiaSharp won)
- SQLite vs PostgreSQL performance (SQLite sufficient)
- Various camera exposure algorithms

## 📖 Related Documentation

- [System.CommandLine Documentation](https://learn.microsoft.com/en-us/dotnet/standard/commandline/)
- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [GPIO Testing Guide](../../docs/guides/hardware-simulation-improvements.md)
- [FITS File Format](https://fits.gsfc.nasa.gov/)

## 💡 Contributing

Playground is a free-form area! Guidelines:
1. **Create new folders** for each experiment
2. **Add README** to explain what you're testing
3. **Share findings** via GitHub issues/discussions
4. **Clean up** old experiments periodically
5. **Graduate valuable code** to proper projects

**Remember**: Playground code doesn't need to be perfect - it needs to teach us something!
