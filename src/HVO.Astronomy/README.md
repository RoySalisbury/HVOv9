# HVO.Astronomy - FITS I/O and Astronomical Computing

[![Astronomy Domain CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/astronomy.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/astronomy.yml)

Domain providing FITS (Flexible Image Transport System) file I/O and astronomical data processing capabilities for the HVOv9 observatory suite.

## 📦 Domain Overview

The **HVO.Astronomy** domain enables:
- **FITS file reading/writing** - Industry-standard astronomical image format
- **Native CFITSIO integration** - High-performance C library via P/Invoke
- **Cross-platform support** - Linux (Raspberry Pi), macOS, Windows
- **Header metadata extraction** - Access telescope, camera, exposure info
- **Image data access** - Read pixel values for analysis/display

## 📁 Projects in This Domain

### HVO.Astronomy.CFITSIO
.NET wrapper around the CFITSIO C library:
- P/Invoke bindings to CFITSIO functions
- Type-safe C# APIs for FITS operations
- Automatic memory management
- Error handling and status codes

### HVO.Astronomy.CFITSIO.NativeAssets
Platform-specific CFITSIO shared libraries:
- `libcfitsio.so` (Linux ARM64/x64)
- `libcfitsio.dylib` (macOS ARM64/x64)
- `cfitsio.dll` (Windows x64)
- NuGet `runtimes/` folder structure for automatic deployment

### HVO.Astronomy.CFITSIO.Tests
Comprehensive unit and integration tests:
- FITS file reading/writing
- Header parsing and manipulation
- Image data extraction
- Cross-platform compatibility

### HVO.Astronomy.CFITSIO.VersionProbe
Diagnostic utility to verify CFITSIO installation:
- Reports library version
- Tests basic FITS operations
- Validates native library loading

## 🔑 Key Features

### FITS File Reading

```csharp
using HVO.Astronomy.CFITSIO;

// Open FITS file
using var fits = FitsFile.Open("M31_Light_300s_001.fits");

// Read image dimensions
var (width, height) = fits.GetImageDimensions();
Console.WriteLine($"Image size: {width}x{height}");

// Read pixel data
var pixels = fits.ReadImageData<ushort>();

// Access header keywords
var exposure = fits.GetKeywordValue<double>("EXPOSURE");
var temperature = fits.GetKeywordValue<double>("CCD-TEMP");
Console.WriteLine($"Exposure: {exposure}s at {temperature}°C");
```

### FITS File Writing

```csharp
// Create new FITS file
using var fits = FitsFile.Create("output.fits", width: 1920, height: 1080);

// Write image data
fits.WriteImageData(pixelArray);

// Add header keywords
fits.SetKeywordValue("TELESCOP", "Hualapai Valley Observatory");
fits.SetKeywordValue("INSTRUME", "ZWO ASI183MM Pro");
fits.SetKeywordValue("EXPOSURE", 300.0);
fits.SetKeywordValue("CCD-TEMP", -10.0);

// File automatically saved on Dispose()
```

### Header Metadata Access

```csharp
// Read all standard headers
var headers = fits.GetAllHeaders();
foreach (var (keyword, value, comment) in headers)
{
    Console.WriteLine($"{keyword} = {value} / {comment}");
}

// Common astronomy headers
var telescope = fits.GetKeywordValue<string>("TELESCOP");
var filter = fits.GetKeywordValue<string>("FILTER");
var ra = fits.GetKeywordValue<double>("RA");  // Right Ascension
var dec = fits.GetKeywordValue<double>("DEC"); // Declination
var dateObs = fits.GetKeywordValue<DateTime>("DATE-OBS");
```

## 🎓 Usage Examples

### Sky Monitor Integration

```csharp
public class SkyMonitor
{
    public async Task<Result<FitsMetadata>> CaptureAndAnalyzeAsync()
    {
        try
        {
            // Capture image from camera (returns FITS file path)
            var fitsPath = await _camera.CaptureAsync(exposure: 30.0);
            
            // Open and analyze
            using var fits = FitsFile.Open(fitsPath);
            
            var metadata = new FitsMetadata
            {
                Width = fits.GetImageDimensions().width,
                Height = fits.GetImageDimensions().height,
                Exposure = fits.GetKeywordValue<double>("EXPOSURE"),
                Temperature = fits.GetKeywordValue<double>("CCD-TEMP"),
                Timestamp = fits.GetKeywordValue<DateTime>("DATE-OBS")
            };
            
            // Extract pixel statistics
            var pixels = fits.ReadImageData<ushort>();
            metadata.MeanValue = pixels.Average(p => (double)p);
            metadata.MaxValue = pixels.Max();
            
            return Result<FitsMetadata>.Success(metadata);
        }
        catch (Exception ex)
        {
            return Result<FitsMetadata>.Failure(ex);
        }
    }
}
```

### Image Stacking Pipeline

```csharp
public class ImageStacker
{
    public Result<string> StackImages(string[] fitsFiles, string outputPath)
    {
        try
        {
            // Open all input files
            var images = fitsFiles.Select(FitsFile.Open).ToArray();
            
            // Verify all same dimensions
            var (width, height) = images[0].GetImageDimensions();
            if (!images.All(f => f.GetImageDimensions() == (width, height)))
                throw new InvalidOperationException("Images must have same dimensions");
            
            // Read pixel data
            var pixelArrays = images.Select(f => f.ReadImageData<ushort>()).ToArray();
            
            // Stack (average)
            var stacked = new ushort[width * height];
            for (int i = 0; i < stacked.Length; i++)
            {
                var sum = pixelArrays.Sum(arr => (int)arr[i]);
                stacked[i] = (ushort)(sum / pixelArrays.Length);
            }
            
            // Write output
            using var output = FitsFile.Create(outputPath, width, height);
            output.WriteImageData(stacked);
            output.SetKeywordValue("STACKCNT", fitsFiles.Length);
            
            // Cleanup
            foreach (var img in images) img.Dispose();
            
            return Result<string>.Success(outputPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(ex);
        }
    }
}
```

## 🧪 Testing

### Run Domain Tests
```bash
cd src/HVO.Astronomy
dotnet test
```

### Test Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

### Build Domain Solution
```bash
cd src/HVO.Astronomy
dotnet build HVO.Astronomy.sln
```

### Version Probe
```bash
cd src/HVO.Astronomy/HVO.Astronomy.CFITSIO.VersionProbe
dotnet run
```

## ⚙️ Native Library Deployment

### NuGet Package Structure
```
HVO.Astronomy.CFITSIO.NativeAssets/
└── runtimes/
    ├── linux-arm64/native/libcfitsio.so
    ├── linux-x64/native/libcfitsio.so
    ├── osx-arm64/native/libcfitsio.dylib
    ├── osx-x64/native/libcfitsio.dylib
    └── win-x64/native/cfitsio.dll
```

### Automatic Deployment
Native libraries are automatically copied to output directory via NuGet package reference:
```xml
<PackageReference Include="HVO.Astronomy.CFITSIO.NativeAssets" />
```

## 🔗 Dependencies

### HVO.Astronomy.CFITSIO
- `HVO` - Core library (Result<T>, Option<T>)
- CFITSIO native library (provided by NativeAssets package)

### HVO.Astronomy.CFITSIO.NativeAssets
- No managed dependencies (native libraries only)

## 📚 Used By

- `HVO.SkyMonitorV5.RPi` - FITS image capture and analysis
- `HVO.Playground.CLI` - FITS file experiments and testing
- Future: Image processing pipeline, astrometry, photometry

## 🎨 Design Considerations

### Why CFITSIO?
- **Industry standard** - Used by NASA, ESA, major observatories
- **Performance** - Optimized C code for large images
- **Feature complete** - Full FITS specification support
- **Well maintained** - Active development since 1995

### P/Invoke vs. Managed
- **P/Invoke chosen** for performance and feature parity
- Alternative: Pure C# FITS library (slower, incomplete features)
- Future: Consider [nom.tam.fits](https://github.com/Nom-TAM/nom-tam-fits) port

### Memory Management
All FITS file handles properly disposed:
```csharp
public class FitsFile : IDisposable
{
    private IntPtr _fptr;
    
    public void Dispose()
    {
        if (_fptr != IntPtr.Zero)
        {
            CFITSIO.fits_close_file(_fptr, out _);
            _fptr = IntPtr.Zero;
        }
    }
}
```

## 🔄 Future Enhancements

- [ ] Add World Coordinate System (WCS) support
- [ ] Implement astrometric plate solving
- [ ] Add image calibration utilities (dark/flat/bias)
- [ ] Support multi-extension FITS (MEF) files
- [ ] Add FITS table (binary/ASCII) support
- [ ] Implement parallel FITS processing
- [ ] Create FITS thumbnail generator
- [ ] Add FITS header validation

## 📖 Related Documentation

- [FITS Format Specification](https://fits.gsfc.nasa.gov/fits_standard.html)
- [CFITSIO Documentation](https://heasarc.gsfc.nasa.gov/fitsio/)
- [Astronomical Data Formats](https://www.astropy.org/astropy-data/)
