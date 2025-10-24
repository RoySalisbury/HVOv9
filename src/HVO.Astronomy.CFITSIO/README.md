## HVO.Astronomy.CFITSIO

A modern .NET 9 library for working with FITS files (Flexible Image Transport System) via CFITSIO. It ships:

- Source-generated P/Invoke bindings to CFITSIO with UTF‑8 marshalling and Cdecl calling convention
- A safe, high-level managed API for images, headers, and compression
- Optional SkiaSharp helpers to convert between FITS and common bitmap formats
- Multi-RID native binaries packaged under `runtimes/**/native` for Linux, macOS, and Windows

All public methods follow a non-throwing Result<T> pattern for reliable error handling at call sites.

License: BSD-3-Clause

---

## Table of contents

- What is included
- Installation
- Design and structure
- Error handling (Result<T>)
- Quick start
- Usage examples
    - Open/create files and HDU navigation
    - Image I/O (U16/U8)
    - Header keywords (write/read)
    - Compression (whole file and per-HDU policy)
    - WCS stamping (simple TAN and CD matrix)
    - SkiaSharp integration (optional)
- Platform and packaging notes

---

## What is included

- `Interop/CFitsIO.cs`: Source-generated [LibraryImport] interop for CFITSIO
- Managed API (one class per file following project standards):
    - `FitsFile.cs`: Main FITS file wrapper with HDU navigation and I/O
    - `FitsImage.cs`: Static helpers for U16/U8 image operations
    - `FitsCommonKeywords.cs`: Standard FITS keyword constants and utilities
    - `FitsHeaderBuilder.cs`: Fluent builder for common header keywords
    - `WcsHeaderBuilder.cs`: Fluent builder for WCS header keywords
    - `FitsCompression.cs`: Compression algorithm enumeration
    - `FitsCompressionPolicy.cs`: Compression configuration class
    - `SkiaFitsExtensions.cs`: Optional SkiaSharp integration (conditional compilation)

The project enables overflow checking in Debug and Release, uses SafeHandle for native lifetime, and standardizes UTF‑8 string marshalling.

## Installation

- NuGet (when published):
    - `dotnet add package HVO.Astronomy.CFITSIO`
- From source:
    - Add a project reference to `HVO.Astronomy.CFITSIO.csproj`
    - Ensure your app targets .NET 9.0 or later

No runtime setup is required—the package includes native CFITSIO binaries for all major platforms.

## Design and structure

- Namespace: `HVO.Astronomy.CFITSIO`
- Core types (one class per file):
    - `FitsFile`: Safe wrapper over a native `fitsfile*` using SafeHandle
    - `FitsImage`: Static helpers for grayscale images (U16, U8)
    - `FitsCommonKeywords`: Standard FITS keyword constants and ISO timestamp helpers
    - `FitsHeaderBuilder`: Fluent utility to stamp common keywords
    - `WcsHeaderBuilder`: Fluent utility to stamp WCS keywords
    - `FitsCompression`: Enum of compression algorithms (None, Rice, Gzip, etc.)
    - `FitsCompressionPolicy`: Configuration class for compression parameters
    - `SkiaFitsExtensions` (optional, under `HAS_SKIA`): FITS ↔ SkiaSharp bitmap conversions
- Error model: `HVO.Result<T>` and `HVO.Result<T,TEnum>` provide non-throwing operation results

## Error handling: Result<T>

All public APIs return `Result<T>` instead of throwing. Typical usage patterns:

```csharp
var r = FitsFile.Open("data.fits");
if (r.IsFailure) { Console.Error.WriteLine(r.Error); return; }
using var fits = r.Value;

var ip = fits.GetImageParameters();
(int bitpix, int naxis, long[] axes) = ip.Match(
    success: v => v,
    failure: _ => throw new InvalidOperationException("Failed to get image params", ip.Error)
);
```

You can also propagate failures up by returning `Result<T>` from your own methods.

## Quick start

Create a 16-bit image and write it to a new FITS file:

```csharp
using HVO.Astronomy.CFITSIO;

int width = 1024, height = 768;
var pixels = new ushort[width * height];
// fill pixels...

var rc = FitsFile.Create("!out.fits");
if (rc.IsFailure) throw rc.Error!;
using var fits = rc.Value;

var w = FitsImage.WriteU16(fits, width, height, pixels);
if (w.IsFailure) throw w.Error!;

// Optional: add a few header keywords
fits.WriteKeyString("OBSERVER", "HVO");
fits.WriteKeyDouble("EXPTIME", 120.0, -1, "Exposure time (s)");
```

Read an image back:

```csharp
var ro = FitsFile.Open("out.fits");
using var f = ro.Value; // throws if failure; or check ro.IsSuccessful

var rimg = FitsImage.ReadU16(f);
var (buf, w, h) = rimg.Value;
```

## Usage examples

### Open/create files and navigate HDUs

```csharp
var rOpen = FitsFile.Open("cube.fits");
if (rOpen.IsFailure) throw rOpen.Error!;
using var ff = rOpen.Value;

var hduInfo = ff.GetCurrentHduInfo().Value; // (HduType, AbsoluteHduNumber)
int count = ff.GetNumberOfHdus().Value;

// Move to absolute HDU
int newType = ff.MoveToHdu(2).Value;

// Move relatively (e.g., next HDU)
_ = ff.MoveBy(+1).Value;
```

### Image I/O (U16/U8)

```csharp
// Write U16 image
using (var c = FitsFile.Create("!u16.fits").Value)
{
    var pixels = new ushort[640*480];
    _ = FitsImage.WriteU16(c, 640, 480, pixels).Value;
}

// Read U8 image
using (var o = FitsFile.Open("u8.fits").Value)
{
    var (data, w, h) = FitsImage.ReadU8(o).Value;
}
```

Direct pixel access on the current HDU:

```csharp
// After creating an image HDU, you can write spans directly
var mk = ff.CreateImageHdu(CFitsIO.USHORT_IMG, 800, 600);
_ = ff.SetScale(1.0, 32768.0).Value; // U16 convention

Span<ushort> row = stackalloc ushort[800];
// fill row...
_ = ff.WritePixelsU16(firstElementIndex: 1, source: row).Value;
```

### Header keywords (write/read)

```csharp
// Write
_ = ff.WriteKeyString("FILTER", "L", "Luminance").Value;
_ = ff.WriteKeyInt32("BINNING", 2).Value;
_ = ff.WriteKeyBoolean("GUIDING", false).Value;

// Read (nullable results)
var rFilter = ff.TryGetKeyString("FILTER");
string? filter = rFilter.Value; // may be null if not present

int? binning = ff.TryGetKeyInt32("BINNING").Value;
double? exptime = ff.TryGetKeyDouble("EXPTIME").Value;

// Raw cards
var cards = ff.ReadAllHeaderCards().Value; // IReadOnlyList<string>
```

Fluent header stamping with builders:

```csharp
var hb = new FitsHeaderBuilder(ff)
    .SetString("OBSERVER", "HVO")
    .SetDouble("EXPTIME", 120.0, -1)
    .SetBoolean("TRACKING", true)
    .SetScale(1.0, 32768.0);
```

### Compression

Whole-file compression (all image HDUs):

```csharp
_ = ff.CompressTo("!compressed.fits").Value;
```

Per-HDU compression policy:

```csharp
var policy = new FitsCompressionPolicy
{
    Compression = FitsCompression.Rice,
    TileDimensions = new long[] { 32, 32 },
    Parameters = Array.Empty<float>(),
    WriteChecksum = true
};

_ = ff.ApplyCompressionPolicyToCurrentHdu(policy).Value;
```

### WCS stamping (TAN)

Use the WCS builder to set a minimal TAN header with a CD matrix:

```csharp
var wcs = new WcsHeaderBuilder(ff)
    .SetTanWithCdMatrix(
            referenceWorldLongitudeDegrees: 210.123456,
            referenceWorldLatitudeDegrees: -2.345678,
            referencePixelX: 1024.5,
            referencePixelY: 768.5,
            cd11: -2.5/3600.0, cd12: 0.0,
            cd21: 0.0,        cd22: 2.5/3600.0,
            unitsAxis1: "deg",
            unitsAxis2: "deg");
```

To read WCS back, use `TryGetKey*` and reconstruct as needed (this library doesn’t perform celestial transforms; for that, consider binding WCSLIB).

### SkiaSharp integration (optional)

When compiled with `HAS_SKIA` and a reference to SkiaSharp, use the helpers:

```csharp
using HVO.Astronomy.CFITSIO;
using SkiaSharp;

// Save a grayscale SKBitmap to FITS (U16)
var bmp = new SKBitmap(new SKImageInfo(320, 240, SKColorType.Gray8));
var s = bmp.SaveAsFitsU16("!img.fits", overwrite: true);
if (s.IsFailure) throw s.Error!;

// Load a FITS as Gray8 SKBitmap (downconvert from U16 if needed)
var rBmp = SkiaFitsExtensions.LoadFitsToBitmap("img.fits", preferU16: true);
var bitmap = rBmp.Value;
```

## Platform and packaging notes

- Native binaries live under `runtimes/<rid>/native` so P/Invoke resolves without manual deployment
- The interop layer uses LibraryImport (source generators), UnmanagedCallConv with Cdecl, and UTF‑8 strings
- SafeHandle ensures native `fitsfile*` is closed reliably; dispose `FitsFile` when done

If you build a NuGet package via `dotnet pack`, the nupkg will include the native payload and README.

---

## Troubleshooting

- “Result<T> does not deconstruct”: Access the `.Value` or use `.Match` to unwrap the tuple value returned by methods like `GetImageParameters()`
- “Type used in a using statement must implement IDisposable”: Ensure you’re unwrapping `Result<FitsFile>` before using `using var`
- Linux/macOS library load issues: confirm the application is loading the correct RID folder and that executable has permissions to load native libs

---

## Contributing

Issues and PRs are welcome. Please follow the repository coding standards (logging, Result<T> usage, and .NET 9 features) outlined in the workspace docs.