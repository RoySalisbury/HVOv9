# SkyMonitor V5 Unified Format Configuration - Implementation Plan

## Executive Summary

Unify the frame export configuration model to support any format (JPEG, PNG, FITS, TIFF, XISF) at any of the 4 export points (Raw/Archive, Raw/Delivery, Processed/Archive, Processed/Delivery). Eliminate the separate `FitsExportOptions` in favor of format-specific options embedded in the unified `ImageEncodingSettings` structure.

## Goals

1. **Unified Configuration**: All 4 export points use the same `ImageEncodingSettings` model
2. **Format Extensibility**: Easy to add new formats (TIFF, XISF) without architectural changes
3. **Clean Separation**: UI endpoints always serve raster formats; download endpoints serve archive formats
4. **Backward Compatibility**: Existing configurations continue to work; migration path provided
5. **Color Readiness**: Architecture supports upcoming RGB/color frame processing

## Current State

### Problems with Current Design

- ✗ Separate `FitsExportOptions` with boolean flags (`EnableForRaw`, `EnableForProcessed`)
- ✗ FITS configuration scattered across multiple option classes
- ✗ No unified way to configure format per export point
- ✗ Difficult to add new formats (TIFF, XISF, etc.)
- ✗ API logic has special cases for FITS vs raster formats

### Current Configuration Model

```json
{
  "FitsExport": {
    "EnableForRaw": true,
    "EnableForProcessed": false,
    "BitDepth": "U16",
    "Compression": "Rice"
  },
  "FrameExport": {
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Jpeg",  // Only PNG/JPEG supported
        "Quality": 95
      }
    }
  }
}
```

## Target State

### Unified Configuration Model

```json
{
  "FrameExport": {
    "Raw": {
      "ArchiveEncoding": {
        "Format": "Fits",
        "Quality": 100,
        "FitsOptions": {
          "BitDepth": "U16",
          "ImageFormat": "Mono",
          "Compression": "Rice",
          "UnsignedU16": true,
          "WriteChecksum": true
        }
      },
      "DeliveryEncoding": {
        "Format": "Jpeg",
        "Quality": 80
      }
    },
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Fits",
        "Quality": 100,
        "FitsOptions": {
          "BitDepth": "U16",
          "ImageFormat": "Rgb",
          "Compression": "Rice"
        }
      },
      "DeliveryEncoding": {
        "Format": "Png",
        "Quality": 100
      }
    }
  }
}
```

## Implementation Phases

---

## Phase 1: Extend Core Types for Format Support

### Tasks

#### 1.1: Extend ImageEncodingFormat Enum
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Pipeline/ImageEncodingFormat.cs`

```csharp
public enum ImageEncodingFormat
{
    Png = 0,
    Jpeg = 1,
    Fits = 2,
    Tiff = 3,      // Future
    Xisf = 4       // Future
}
```

**Tests**:
- `ImageEncodingFormatTests.AllValues_HaveStringRepresentation()`
- `ImageEncodingFormatTests.Parse_ValidStrings_ReturnsCorrectEnum()`

#### 1.2: Create FitsEncodingOptions Record
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Pipeline/FitsEncodingOptions.cs` (new)

```csharp
namespace HVO.SkyMonitorV5.RPi.Pipeline;

/// <summary>
/// FITS-specific encoding options.
/// </summary>
public sealed record FitsEncodingOptions
{
    /// <summary>Bit depth for FITS images. Default is U16.</summary>
    public FitsBitDepth BitDepth { get; init; } = FitsBitDepth.U16;
    
    /// <summary>Image plane format. Default is Mono (grayscale).</summary>
    public FitsImageFormat ImageFormat { get; init; } = FitsImageFormat.Mono;
    
    /// <summary>When true and BitDepth=U16, write unsigned scaling (BSCALE=1, BZERO=32768).</summary>
    public bool UnsignedU16 { get; init; } = true;
    
    /// <summary>Compression algorithm. None by default.</summary>
    public FitsCompressionKind Compression { get; init; } = FitsCompressionKind.None;
    
    /// <summary>When true, write a FITS checksum.</summary>
    public bool WriteChecksum { get; init; } = true;
}

public enum FitsBitDepth
{
    U8,      // 8-bit unsigned
    U16,     // 16-bit unsigned
    I16,     // 16-bit signed
    I32,     // 32-bit signed
    F32,     // 32-bit float
    F64      // 64-bit float
}

public enum FitsImageFormat
{
    /// <summary>Single-plane monochrome/grayscale image (NAXIS=2).</summary>
    Mono = 0,
    
    /// <summary>3-plane RGB color image (NAXIS=3, RGB planes).</summary>
    Rgb = 1,
    
    /// <summary>4-plane RGBA color image with alpha (NAXIS=3, RGBA planes).</summary>
    Rgba = 2,
    
    /// <summary>Bayer-pattern mosaic (single plane with BAYERPAT header).</summary>
    BayerMosaic = 3
}

public enum FitsCompressionKind
{
    None,
    Rice,
    Gzip1,
    Gzip2,
    HCompress,
    PLio
}
```

**Tests**:
- `FitsEncodingOptionsTests.DefaultValues_AreCorrect()`
- `FitsEncodingOptionsTests.WithModifications_CreatesNewInstance()` (record immutability)
- `FitsImageFormatTests.AllValues_AreValid()`
- `FitsBitDepthTests.AllValues_AreValid()`

#### 1.3: Extend ImageEncodingSettings with Format-Specific Options
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Pipeline/ImageEncodingSettings.cs`

```csharp
public sealed record ImageEncodingSettings
{
    public ImageEncodingFormat Format { get; init; }
    public int Quality { get; init; }
    
    /// <summary>FITS-specific encoding options. Only used when Format=Fits.</summary>
    public FitsEncodingOptions? FitsOptions { get; init; }
    
    // Future: JpegOptions, TiffOptions, XisfOptions, etc.
    
    public ImageEncodingSettings(ImageEncodingFormat format, int quality)
    {
        Format = format;
        Quality = quality;
    }
    
    public ImageEncodingSettings() : this(ImageEncodingFormat.Jpeg, 90)
    {
    }
}
```

**Tests**:
- `ImageEncodingSettingsTests.Constructor_SetsPropertiesCorrectly()`
- `ImageEncodingSettingsTests.WithFitsOptions_StoresCorrectly()`
- `ImageEncodingSettingsTests.DefaultConstructor_UsesJpegDefaults()`
- `ImageEncodingSettingsTests.RecordEquality_WorksWithFitsOptions()`

#### 1.4: Update ImageEncodingUtilities
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Pipeline/ImageEncodingUtilities.cs`

Add support for FITS format:
```csharp
public static string ToContentType(ImageEncodingFormat format) => format switch
{
    ImageEncodingFormat.Png => "image/png",
    ImageEncodingFormat.Jpeg => "image/jpeg",
    ImageEncodingFormat.Fits => "application/fits",
    ImageEncodingFormat.Tiff => "image/tiff",
    ImageEncodingFormat.Xisf => "application/xisf",
    _ => throw new NotSupportedException($"Format {format} not supported")
};

public static string? ToFileExtension(ImageEncodingFormat format) => format switch
{
    ImageEncodingFormat.Png => "png",
    ImageEncodingFormat.Jpeg => "jpg",
    ImageEncodingFormat.Fits => "fits",
    ImageEncodingFormat.Tiff => "tif",
    ImageEncodingFormat.Xisf => "xisf",
    _ => null
};
```

**Tests**:
- `ImageEncodingUtilitiesTests.ToContentType_AllFormats_ReturnsCorrectMimeType()`
- `ImageEncodingUtilitiesTests.ToFileExtension_AllFormats_ReturnsCorrectExtension()`
- `ImageEncodingUtilitiesTests.ToContentType_UnsupportedFormat_ThrowsNotSupported()`

---

## Phase 2: Update Configuration Options

### Tasks

#### 2.1: Update FrameExportStageOptions with Improved Documentation
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Options/FrameExportOptions.cs`

Update XML docs to clarify format flexibility:

```csharp
/// <summary>
/// Encoding settings for archive role exports. Can be any supported format (JPEG, PNG, FITS, etc.).
/// </summary>
/// <remarks>
/// Default: JPEG @ 95% for processed frames, FITS U16 for raw frames.
/// Supports format-specific options (e.g., FitsOptions for FITS format).
/// </remarks>
public ImageEncodingSettings ArchiveEncoding { get; set; } = new(ImageEncodingFormat.Jpeg, 95);

/// <summary>
/// Encoding settings for delivery role exports. Can be any supported format.
/// </summary>
/// <remarks>
/// When null, uses ArchiveEncoding. Typically set to a more web-friendly format
/// (e.g., JPEG @ 80%) when archive uses higher-fidelity formats (FITS, PNG lossless).
/// </remarks>
public ImageEncodingSettings? DeliveryEncoding { get; set; }
```

Update normalization to set sensible defaults per stage:

```csharp
internal void Normalize(FrameExportStage stage)
{
    // ... existing code ...
    
    // Ensure archive encoding has stage-appropriate defaults
    if (ArchiveEncoding == null)
    {
        ArchiveEncoding = stage == FrameExportStage.Raw
            ? new ImageEncodingSettings(ImageEncodingFormat.Fits, 100) 
            {
                FitsOptions = new FitsEncodingOptions()
            }
            : new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 95);
    }
}
```

**Tests**:
- `FrameExportOptionsTests.Normalize_RawStage_DefaultsToFits()`
- `FrameExportOptionsTests.Normalize_ProcessedStage_DefaultsToJpeg()`
- `FrameExportOptionsTests.Normalize_PreservesExistingEncodings()`
- `FrameExportOptionsTests.Normalize_NullArchiveEncoding_SetsDefaults()`

#### 2.2: Deprecate FitsExportOptions
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Options/FitsExportOptions.cs`

Mark as obsolete with migration guidance:

```csharp
[Obsolete("Use FrameExportOptions with ImageEncodingSettings.FitsOptions instead. This class will be removed in a future version.")]
public sealed class FitsExportOptions
{
    // ... existing properties ...
}
```

Add migration helper in `FrameExportOptions`:

```csharp
/// <summary>
/// Migrates legacy FitsExportOptions to unified ImageEncodingSettings.
/// </summary>
public void MigrateFromLegacyFitsOptions(FitsExportOptions legacyOptions)
{
    if (legacyOptions.EnableForRaw)
    {
        Raw.ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
        {
            FitsOptions = new FitsEncodingOptions
            {
                BitDepth = legacyOptions.BitDepth,
                Compression = legacyOptions.Compression,
                UnsignedU16 = legacyOptions.UnsignedU16,
                WriteChecksum = legacyOptions.WriteChecksum
            }
        };
    }
    
    if (legacyOptions.EnableForProcessed)
    {
        Processed.ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
        {
            FitsOptions = new FitsEncodingOptions
            {
                BitDepth = legacyOptions.BitDepth,
                Compression = legacyOptions.Compression,
                UnsignedU16 = legacyOptions.UnsignedU16,
                WriteChecksum = legacyOptions.WriteChecksum
            }
        };
    }
}
```

**Tests**:
- `FitsExportOptionsTests.MigrateFromLegacy_EnableForRaw_MigratesToRawArchiveEncoding()`
- `FitsExportOptionsTests.MigrateFromLegacy_EnableForProcessed_MigratesToProcessedArchiveEncoding()`
- `FitsExportOptionsTests.MigrateFromLegacy_AllOptions_PreservesSettings()`

---

## Phase 3: Update Encoder Layer

### Tasks

#### 3.1: Simplify IProcessedFrameEncoder Interface
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Services/IProcessedFrameEncoder.cs`

Remove context parameter (no longer needed - format is in encoding settings):

```csharp
/// <summary>
/// Encodes processed frames into delivery-ready payloads using specified encoding settings.
/// </summary>
public interface IProcessedFrameEncoder
{
    /// <summary>
    /// Encodes the specified <paramref name="frame"/> using the provided encoding settings.
    /// </summary>
    /// <param name="frame">The processed frame to encode.</param>
    /// <param name="encoding">Encoding settings (format, quality, format-specific options). 
    /// If null, uses frame's default encoding.</param>
    /// <returns>The encoded payload, including content type metadata.</returns>
    ProcessedFrameDelivery Encode(ProcessedFrame frame, ImageEncodingSettings? encoding = null);
}
```

**Tests**:
- Update all existing tests to use new signature
- `ProcessedFrameEncoderTests.Encode_NullEncoding_UsesFrameDefaults()`
- `ProcessedFrameEncoderTests.Encode_CustomEncoding_OverridesFrameSettings()`

#### 3.2: Update ProcessedFrameEncoder Implementation
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Services/ProcessedFrameEncoder.cs`

Refactor to use format-based encoding:

```csharp
public ProcessedFrameDelivery Encode(ProcessedFrame frame, ImageEncodingSettings? encoding = null)
{
    if (frame?.ImmutableImage == null)
        throw new ArgumentException("Frame must have an immutable image.", nameof(frame));
    
    // Use custom encoding or fall back to frame's encoding
    var settings = encoding ?? ImageEncodingUtilities.Normalize(frame.Encoding);
    
    return settings.Format switch
    {
        ImageEncodingFormat.Fits => EncodeFits(frame, settings),
        ImageEncodingFormat.Jpeg => EncodeJpeg(frame, settings),
        ImageEncodingFormat.Png => EncodePng(frame, settings),
        ImageEncodingFormat.Tiff => throw new NotSupportedException("TIFF encoding not yet implemented"),
        ImageEncodingFormat.Xisf => throw new NotSupportedException("XISF encoding not yet implemented"),
        _ => throw new NotSupportedException($"Format {settings.Format} not supported")
    };
}

private ProcessedFrameDelivery EncodeFits(ProcessedFrame frame, ImageEncodingSettings settings)
{
    var fitsOptions = settings.FitsOptions ?? new FitsEncodingOptions();
    
    return fitsOptions.ImageFormat switch
    {
        FitsImageFormat.Mono => EncodeFitsMono(frame, fitsOptions),
        FitsImageFormat.Rgb => throw new NotSupportedException("RGB FITS encoding not yet implemented"),
        FitsImageFormat.Rgba => throw new NotSupportedException("RGBA FITS encoding not yet implemented"),
        FitsImageFormat.BayerMosaic => throw new NotSupportedException("Bayer mosaic FITS encoding not yet implemented"),
        _ => throw new NotSupportedException($"FITS image format {fitsOptions.ImageFormat} not supported")
    };
}

private ProcessedFrameDelivery EncodeFitsMono(ProcessedFrame frame, FitsEncodingOptions options)
{
    // Use existing IFitsFrameEncoder
    var rig = _rigAdapter.ActiveRig;
    
    // Convert FitsEncodingOptions to legacy FitsExportOptions temporarily
    var legacyOptions = new FitsExportOptions
    {
        BitDepth = options.BitDepth,
        Compression = options.Compression,
        UnsignedU16 = options.UnsignedU16,
        WriteChecksum = options.WriteChecksum
    };
    
    var delivery = _fitsEncoder.EncodeProcessed(frame, rig, legacyOptions);
    return new ProcessedFrameDelivery(delivery.Payload, "application/fits", "fits");
}

private ProcessedFrameDelivery EncodeJpeg(ProcessedFrame frame, ImageEncodingSettings settings)
{
    using var data = frame.ImmutableImage.Encode(SKEncodedImageFormat.Jpeg, settings.Quality);
    if (data == null)
        throw new InvalidOperationException($"Failed to encode frame {frame.FrameId} as JPEG.");
    
    return new ProcessedFrameDelivery(data.ToArray(), "image/jpeg", "jpg");
}

private ProcessedFrameDelivery EncodePng(ProcessedFrame frame, ImageEncodingSettings settings)
{
    using var data = frame.ImmutableImage.Encode(SKEncodedImageFormat.Png, settings.Quality);
    if (data == null)
        throw new InvalidOperationException($"Failed to encode frame {frame.FrameId} as PNG.");
    
    return new ProcessedFrameDelivery(data.ToArray(), "image/png", "png");
}
```

**Tests**:
- `ProcessedFrameEncoderTests.Encode_FitsFormat_UsesFitsEncoder()`
- `ProcessedFrameEncoderTests.Encode_FitsWithOptions_PassesOptionsCorrectly()`
- `ProcessedFrameEncoderTests.Encode_JpegFormat_ReturnsJpegPayload()`
- `ProcessedFrameEncoderTests.Encode_PngFormat_ReturnsPngPayload()`
- `ProcessedFrameEncoderTests.Encode_UnsupportedFormat_ThrowsNotSupported()`
- `ProcessedFrameEncoderTests.Encode_FitsRgbFormat_ThrowsNotSupported()` (until implemented)
- `ProcessedFrameEncoderTests.Encode_TiffFormat_ThrowsNotSupported()`

#### 3.3: Create IRawFrameEncoder Interface
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Services/IRawFrameEncoder.cs` (new)

Mirror the processed encoder for consistency:

```csharp
public interface IRawFrameEncoder
{
    /// <summary>
    /// Encodes a raw frame using the specified encoding settings.
    /// </summary>
    RawFrameDelivery Encode(CapturedImage capture, RigSpec rig, ImageEncodingSettings? encoding = null);
}

public readonly record struct RawFrameDelivery(
    ReadOnlyMemory<byte> Payload,
    string ContentType,
    string? FileExtension);
```

#### 3.4: Implement RawFrameEncoder
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Services/RawFrameEncoder.cs` (new)

```csharp
public class RawFrameEncoder : IRawFrameEncoder
{
    private readonly IFitsFrameEncoder _fitsEncoder;
    private readonly ILogger<RawFrameEncoder> _logger;
    
    public RawFrameDelivery Encode(CapturedImage capture, RigSpec rig, ImageEncodingSettings? encoding = null)
    {
        // Default raw encoding: FITS U16
        encoding ??= new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
        {
            FitsOptions = new FitsEncodingOptions()
        };
        
        return encoding.Format switch
        {
            ImageEncodingFormat.Fits => EncodeFits(capture, rig, encoding),
            ImageEncodingFormat.Jpeg => EncodeJpeg(capture, encoding),
            ImageEncodingFormat.Png => EncodePng(capture, encoding),
            _ => throw new NotSupportedException($"Format {encoding.Format} not supported for raw frames")
        };
    }
    
    // ... similar implementation to ProcessedFrameEncoder
}
```

**Tests**:
- `RawFrameEncoderTests.Encode_DefaultEncoding_UsesFits()`
- `RawFrameEncoderTests.Encode_FitsFormat_CallsFitsEncoder()`
- `RawFrameEncoderTests.Encode_JpegFormat_ReturnsJpegPayload()`
- `RawFrameEncoderTests.Encode_FitsWithBayerFormat_ThrowsNotSupported()`

---

## Phase 4: Update Publisher and API Layer

### Tasks

#### 4.1: Update FrameExportPublisher to Use Unified Encoding
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Exports/FrameExportPublisher.cs`

Simplify to use stage encoding directly:

```csharp
public void PublishRawFrame(
    int frameNumber,
    CapturedImage capture,
    RigSpec rig,
    double? captureMilliseconds,
    DateTimeOffset stageTimestampUtc)
{
    try
    {
        var exportOptions = _exportOptions.CurrentValue;
        var rawOptions = exportOptions.Raw;
        var encoding = rawOptions.ArchiveEncoding;
        
        // Use raw encoder with configured encoding
        var delivery = _rawEncoder.Encode(capture, rig, encoding);
        
        var metadata = FrameExportMetadataBuilder.FromRaw(
            capture, rig, stageTimestampUtc,
            queueLatencyMilliseconds: captureMilliseconds,
            processingMilliseconds: null,
            rawImageDescriptor: null,
            payloadContentType: delivery.ContentType,
            payloadExtension: delivery.FileExtension ?? "bin");
        
        var envelope = new FrameExportEnvelope(
            capture.FrameId,
            FrameExportStage.Raw,
            metadata,
            delivery.Payload,
            delivery.ContentType,
            delivery.FileExtension);
        
        _dispatcher.TryEnqueue(envelope);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to publish raw frame {FrameNumber} ({FrameId}).",
            frameNumber, capture.FrameId);
    }
}

public void PublishProcessedFrame(
    int frameNumber,
    FrameStackResult stackResult,
    ProcessedFrame processedFrame,
    RigSpec rig,
    double? queueLatencyMilliseconds,
    double? processingMilliseconds,
    DateTimeOffset stageTimestampUtc)
{
    try
    {
        var exportOptions = _exportOptions.CurrentValue;
        var processedOptions = exportOptions.Processed;
        var encoding = processedOptions.ArchiveEncoding;
        
        // Use processed encoder with configured encoding
        var delivery = _processedFrameEncoder.Encode(processedFrame, encoding);
        
        var metadata = FrameExportMetadataBuilder.FromProcessed(
            processedFrame, stackResult.Context, rig, stageTimestampUtc,
            queueLatencyMilliseconds, processingMilliseconds,
            payloadContentType: delivery.ContentType,
            payloadExtension: delivery.FileExtension ?? "bin");
        
        QueueArchiveIngestion(metadata, delivery.Payload.ToArray(), 
            delivery.ContentType, delivery.FileExtension);
        
        var envelope = new FrameExportEnvelope(
            processedFrame.FrameId,
            FrameExportStage.Processed,
            metadata,
            delivery.Payload,
            delivery.ContentType,
            delivery.FileExtension);
        
        _dispatcher.TryEnqueue(envelope);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to publish processed frame {FrameNumber} ({FrameId}).",
            frameNumber, processedFrame.FrameId);
    }
}
```

**Tests**:
- `FrameExportPublisherTests.PublishRawFrame_UsesRawArchiveEncoding()`
- `FrameExportPublisherTests.PublishProcessedFrame_UsesProcessedArchiveEncoding()`
- `FrameExportPublisherTests.PublishRawFrame_FitsEncoding_CreatesFitsEnvelope()`
- `FrameExportPublisherTests.PublishProcessedFrame_JpegEncoding_CreatesJpegEnvelope()`

#### 4.2: Update AllSkyController for UI Display Endpoints
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Controllers/v1_0/AllSkyController.cs`

Ensure UI endpoints always serve raster formats:

```csharp
/// <summary>
/// Get the latest processed frame for display (always JPEG/PNG).
/// </summary>
[HttpGet("frame/latest")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status500InternalServerError)]
public IActionResult GetLatestFrame()
{
    // UI display: always use display-optimized format
    var uiEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 95);
    
    var frame = _frameProvider.GetLatestProcessedFrame();
    if (frame == null)
        return NotFound("No processed frame available.");
    
    var delivery = _processedFrameEncoder.Encode(frame, uiEncoding);
    return File(delivery.Payload.ToArray(), delivery.ContentType);
}

/// <summary>
/// Get frame thumbnail for timeline/grid display (always small JPEG).
/// </summary>
[HttpGet("frame/{frameId:guid}/thumbnail")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public IActionResult GetThumbnail(Guid frameId)
{
    var thumbnailEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 75);
    
    var frame = _frameProvider.GetProcessedFrame(frameId);
    if (frame == null)
        return NotFound();
    
    // TODO: Add thumbnail generation (resize to 320x240 or similar)
    var delivery = _processedFrameEncoder.Encode(frame, thumbnailEncoding);
    return File(delivery.Payload.ToArray(), delivery.ContentType);
}

/// <summary>
/// Get frame preview for detail view (high-quality PNG for pixel inspection).
/// </summary>
[HttpGet("frame/{frameId:guid}/preview")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public IActionResult GetPreview(Guid frameId)
{
    var previewEncoding = new ImageEncodingSettings(ImageEncodingFormat.Png, 100);
    
    var frame = _frameProvider.GetProcessedFrame(frameId);
    if (frame == null)
        return NotFound();
    
    var delivery = _processedFrameEncoder.Encode(frame, previewEncoding);
    return File(delivery.Payload.ToArray(), delivery.ContentType);
}
```

**Tests**:
- `AllSkyControllerTests.GetLatestFrame_ReturnsJpeg()`
- `AllSkyControllerTests.GetThumbnail_ReturnsSmallJpeg()`
- `AllSkyControllerTests.GetPreview_ReturnsHighQualityPng()`
- `AllSkyControllerTests.GetLatestFrame_NoFrame_Returns404()`

#### 4.3: Add Download Endpoints for Archive Data
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Controllers/v1_0/AllSkyController.cs`

```csharp
/// <summary>
/// Download raw frame in configured archive format (typically FITS).
/// </summary>
[HttpGet("frame/{frameId:guid}/download/raw")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public IActionResult DownloadRaw(Guid frameId, [FromQuery] string? format = null, [FromQuery] int? quality = null)
{
    var capture = _frameProvider.GetRawFrame(frameId);
    if (capture == null)
        return NotFound();
    
    var exportOptions = _exportOptions.CurrentValue;
    var rawOptions = exportOptions.Raw;
    
    // Use archive encoding by default, allow format override
    var encoding = format != null
        ? CreateEncodingFromQuery(format, quality ?? rawOptions.ArchiveEncoding.Quality)
        : rawOptions.ArchiveEncoding;
    
    var delivery = _rawEncoder.Encode(capture, _rigAdapter.ActiveRig, encoding);
    
    var fileName = $"{frameId}_raw.{delivery.FileExtension}";
    Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
    
    return File(delivery.Payload.ToArray(), delivery.ContentType);
}

/// <summary>
/// Download processed frame in configured archive format.
/// </summary>
[HttpGet("frame/{frameId:guid}/download/processed")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public IActionResult DownloadProcessed(Guid frameId, [FromQuery] string? format = null, [FromQuery] int? quality = null)
{
    var frame = _frameProvider.GetProcessedFrame(frameId);
    if (frame == null)
        return NotFound();
    
    var exportOptions = _exportOptions.CurrentValue;
    var processedOptions = exportOptions.Processed;
    
    // Use archive encoding by default, allow format override
    var encoding = format != null
        ? CreateEncodingFromQuery(format, quality ?? processedOptions.ArchiveEncoding.Quality)
        : processedOptions.ArchiveEncoding;
    
    var delivery = _processedFrameEncoder.Encode(frame, encoding);
    
    var fileName = $"{frameId}_processed.{delivery.FileExtension}";
    Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
    
    return File(delivery.Payload.ToArray(), delivery.ContentType);
}

private ImageEncodingSettings CreateEncodingFromQuery(string format, int quality)
{
    var formatEnum = Enum.Parse<ImageEncodingFormat>(format, ignoreCase: true);
    
    return formatEnum switch
    {
        ImageEncodingFormat.Fits => new ImageEncodingSettings(formatEnum, quality)
        {
            FitsOptions = new FitsEncodingOptions() // Use defaults
        },
        _ => new ImageEncodingSettings(formatEnum, quality)
    };
}
```

**Tests**:
- `AllSkyControllerTests.DownloadRaw_DefaultFormat_UsesArchiveEncoding()`
- `AllSkyControllerTests.DownloadRaw_FormatOverride_UsesSpecifiedFormat()`
- `AllSkyControllerTests.DownloadRaw_FitsFormat_ReturnsApplicationFits()`
- `AllSkyControllerTests.DownloadProcessed_DefaultFormat_UsesArchiveEncoding()`
- `AllSkyControllerTests.DownloadProcessed_JpegOverride_ReturnsJpeg()`
- `AllSkyControllerTests.Download_InvalidFrameId_Returns404()`
- `AllSkyControllerTests.Download_SetsContentDispositionHeader()`

---

## Phase 5: Configuration Migration and Backward Compatibility

### Tasks

#### 5.1: Create Configuration Migration Service
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Services/FrameExportConfigurationMigration.cs` (new)

```csharp
public interface IFrameExportConfigurationMigration
{
    /// <summary>
    /// Detects if legacy FITS configuration exists and migrates it to unified format.
    /// </summary>
    void MigrateIfNeeded(FrameExportOptions exportOptions, FitsExportOptions? legacyFitsOptions);
}

public class FrameExportConfigurationMigration : IFrameExportConfigurationMigration
{
    private readonly ILogger<FrameExportConfigurationMigration> _logger;
    
    public void MigrateIfNeeded(FrameExportOptions exportOptions, FitsExportOptions? legacyFitsOptions)
    {
        if (legacyFitsOptions == null)
            return;
        
        // Check if already migrated (if Format=Fits, assume migration done)
        if (exportOptions.Raw.ArchiveEncoding.Format == ImageEncodingFormat.Fits)
        {
            _logger.LogDebug("FITS configuration already migrated.");
            return;
        }
        
        _logger.LogInformation("Migrating legacy FitsExportOptions to unified configuration.");
        
        exportOptions.MigrateFromLegacyFitsOptions(legacyFitsOptions);
        
        _logger.LogInformation("Migration complete. Consider updating appsettings.json to use unified FrameExport configuration.");
    }
}
```

**Tests**:
- `FrameExportConfigurationMigrationTests.MigrateIfNeeded_NoLegacyConfig_DoesNothing()`
- `FrameExportConfigurationMigrationTests.MigrateIfNeeded_LegacyConfig_MigratesToUnified()`
- `FrameExportConfigurationMigrationTests.MigrateIfNeeded_AlreadyMigrated_SkipsMigration()`

#### 5.2: Register Migration in Startup
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Program.cs`

```csharp
// Register migration service
builder.Services.AddSingleton<IFrameExportConfigurationMigration, FrameExportConfigurationMigration>();

// Run migration on startup
builder.Services.AddHostedService<ConfigurationMigrationHostedService>();

// ConfigurationMigrationHostedService.cs (new)
public class ConfigurationMigrationHostedService : IHostedService
{
    private readonly IFrameExportConfigurationMigration _migration;
    private readonly IOptionsMonitor<FrameExportOptions> _exportOptions;
    private readonly IOptionsMonitor<FitsExportOptions> _fitsOptions;
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _migration.MigrateIfNeeded(_exportOptions.CurrentValue, _fitsOptions.CurrentValue);
        return Task.CompletedTask;
    }
    
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

#### 5.3: Update appsettings.json Template
**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/appsettings.json`

Provide example of new unified configuration:

```json
{
  "FrameExport": {
    "Raw": {
      "Enabled": true,
      "ArchiveEncoding": {
        "Format": "Fits",
        "Quality": 100,
        "FitsOptions": {
          "BitDepth": "U16",
          "ImageFormat": "Mono",
          "Compression": "Rice",
          "UnsignedU16": true,
          "WriteChecksum": true
        }
      },
      "DeliveryEncoding": {
        "Format": "Jpeg",
        "Quality": 80
      },
      "PayloadScope": "ArchiveOnly"
    },
    "Processed": {
      "Enabled": true,
      "ArchiveEncoding": {
        "Format": "Jpeg",
        "Quality": 95
      },
      "PayloadScope": "ArchiveOnly"
    }
  }
}
```

---

## Phase 6: Documentation and Examples

### Tasks

#### 6.1: Update Existing Documentation
**File**: `docs/projects/skymonitor-v5-export-encoding.md`

Complete rewrite to reflect unified configuration model.

#### 6.2: Create Format Configuration Guide
**File**: `docs/projects/skymonitor-v5-format-configuration-guide.md` (new)

Comprehensive guide with:
- Format comparison table (FITS vs JPEG vs PNG)
- Use case examples
- Performance considerations
- Quality settings guidance
- Format-specific options reference

#### 6.3: Create Migration Guide
**File**: `docs/projects/skymonitor-v5-fits-migration-guide.md` (new)

Step-by-step guide for users migrating from old configuration:
- Before/after configuration examples
- Automatic vs manual migration
- Breaking changes checklist
- Rollback procedure

---

## Phase 7: Testing Matrix

### Comprehensive Test Coverage

#### Unit Tests (Per Phase)

**Phase 1 Tests** (Core Types):
- ImageEncodingFormat enum tests
- FitsEncodingOptions tests
- ImageEncodingSettings tests
- ImageEncodingUtilities tests
- **Total: ~15 tests**

**Phase 2 Tests** (Configuration):
- FrameExportOptions normalization tests
- Legacy migration tests
- Default value tests
- **Total: ~10 tests**

**Phase 3 Tests** (Encoders):
- ProcessedFrameEncoder format dispatch tests
- RawFrameEncoder format dispatch tests
- FITS encoding tests
- JPEG/PNG encoding tests
- Error handling tests
- **Total: ~25 tests**

**Phase 4 Tests** (Publisher & API):
- FrameExportPublisher encoding tests
- AllSkyController UI endpoint tests
- AllSkyController download endpoint tests
- Format override tests
- **Total: ~20 tests**

**Phase 5 Tests** (Migration):
- Configuration migration tests
- Backward compatibility tests
- **Total: ~5 tests**

#### Integration Tests

```csharp
[TestClass]
public class UnifiedFormatIntegrationTests
{
    [TestMethod]
    public async Task EndToEnd_RawFitsExport_CreatesValidFitsFile()
    {
        // Configure raw FITS export
        var options = new FrameExportOptions
        {
            Raw = new FrameExportStageOptions
            {
                ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
                {
                    FitsOptions = new FitsEncodingOptions 
                    { 
                        BitDepth = FitsBitDepth.U16,
                        Compression = FitsCompressionKind.Rice 
                    }
                }
            }
        };
        
        // Capture, publish, export
        var capture = CaptureTestFrame();
        _publisher.PublishRawFrame(1, capture, TestRig, null, DateTimeOffset.UtcNow);
        
        // Assert FITS file created
        var exportedFile = FindExportedFile("raw", "fits");
        Assert.IsNotNull(exportedFile);
        Assert.IsTrue(IsFitsFile(exportedFile));
    }
    
    [TestMethod]
    public async Task EndToEnd_ProcessedMultipleFormats_CreatesCorrectFiles()
    {
        // Configure processed with archive=FITS, delivery=JPEG
        var options = new FrameExportOptions
        {
            Processed = new FrameExportStageOptions
            {
                ArchiveEncoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100)
                {
                    FitsOptions = new FitsEncodingOptions()
                },
                DeliveryEncoding = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 85),
                PayloadScope = FrameExportPayloadScope.ArchiveAndDelivery
            }
        };
        
        // Process and publish
        var processedFrame = ProcessTestFrame();
        _publisher.PublishProcessedFrame(1, stackResult, processedFrame, TestRig, null, null, DateTimeOffset.UtcNow);
        
        // Assert both formats created
        var archiveFits = FindExportedFile("processed/archive", "fits");
        var deliveryJpeg = FindExportedFile("processed/delivery", "jpg");
        
        Assert.IsNotNull(archiveFits);
        Assert.IsNotNull(deliveryJpeg);
        Assert.IsTrue(IsFitsFile(archiveFits));
        Assert.IsTrue(IsJpegFile(deliveryJpeg));
    }
    
    [TestMethod]
    public async Task API_DownloadRaw_DefaultFormat_ReturnsConfiguredFormat()
    {
        // ... test API respects configuration
    }
    
    [TestMethod]
    public async Task API_DownloadWithFormatOverride_ReturnsRequestedFormat()
    {
        // ... test format override works
    }
}
```

#### Configuration Tests

```csharp
[TestClass]
public class ConfigurationValidationTests
{
    [TestMethod]
    public void ValidConfiguration_AllFormats_PassesValidation()
    {
        foreach (var format in Enum.GetValues<ImageEncodingFormat>())
        {
            var encoding = CreateEncodingForFormat(format);
            var options = new FrameExportOptions
            {
                Raw = { ArchiveEncoding = encoding },
                Processed = { ArchiveEncoding = encoding }
            };
            
            Assert.IsTrue(options.IsValid());
        }
    }
    
    [TestMethod]
    public void Configuration_FitsWithoutOptions_UsesDefaults()
    {
        var encoding = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100);
        Assert.IsNull(encoding.FitsOptions);
        
        // Encoder should use defaults when FitsOptions is null
        var delivery = _encoder.Encode(frame, encoding);
        Assert.IsNotNull(delivery);
    }
}
```

#### Performance Tests

```csharp
[TestClass]
public class FormatPerformanceTests
{
    [TestMethod]
    public void Encoding_AllFormats_CompletesWithinTimeout()
    {
        var testFrame = CreateLargeTestFrame(1920, 1080);
        var timeout = TimeSpan.FromSeconds(5);
        
        foreach (var format in new[] { ImageEncodingFormat.Jpeg, ImageEncodingFormat.Png, ImageEncodingFormat.Fits })
        {
            var encoding = CreateEncodingForFormat(format);
            
            var sw = Stopwatch.StartNew();
            var delivery = _encoder.Encode(testFrame, encoding);
            sw.Stop();
            
            Assert.IsLessThan(sw.Elapsed, timeout, $"{format} encoding took too long: {sw.Elapsed}");
            Assert.IsGreaterThan(delivery.Payload.Length, 0);
        }
    }
}
```

---

## Rollout Strategy

### Development Sequence

1. **Phase 1-2**: Core types and configuration (1-2 days)
   - Can be completed without breaking existing code
   - Mark legacy types as obsolete
   
2. **Phase 3**: Encoder updates (2-3 days)
   - Refactor existing encoders
   - Add comprehensive tests
   
3. **Phase 4**: Publisher and API (2-3 days)
   - Update export pipeline
   - Add download endpoints
   
4. **Phase 5**: Migration (1 day)
   - Implement auto-migration
   - Test backward compatibility
   
5. **Phase 6**: Documentation (1 day)
   - Update all docs
   - Create migration guide
   
6. **Phase 7**: Integration testing (1-2 days)
   - End-to-end tests
   - Performance validation

**Total Estimated Time**: 8-12 days

### Feature Flags (Optional)

```json
{
  "SkiaPipelineFeature": {
    "EnableUnifiedFormatConfiguration": true
  }
}
```

Allow rollback to legacy configuration if issues found.

---

## Success Criteria

### Functional Requirements
✅ All 4 export points support all formats (JPEG, PNG, FITS)  
✅ Format-specific options work correctly (FitsOptions)  
✅ Legacy FITS configuration auto-migrates  
✅ UI endpoints always serve raster formats  
✅ Download endpoints serve archive formats  
✅ Format overrides work via query parameters  

### Non-Functional Requirements
✅ No performance regression vs current implementation  
✅ All existing tests pass with updated signatures  
✅ 100% backward compatibility with migration  
✅ Comprehensive test coverage (>90% code coverage for new code)  
✅ Complete documentation with examples  

### Testing Requirements
✅ 75+ new unit tests covering all scenarios  
✅ 10+ integration tests covering end-to-end workflows  
✅ Configuration validation tests  
✅ Performance benchmarks  
✅ Migration tests  

---

## Risk Mitigation

### Risks & Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Breaking existing deployments | High | Low | Auto-migration + feature flag |
| Performance regression | Medium | Low | Performance tests + benchmarking |
| Complex encoder refactoring | Medium | Medium | Incremental changes + thorough testing |
| Missing edge cases | Medium | Medium | Comprehensive test matrix |
| Documentation gaps | Low | Medium | Dedicated docs phase |

### Rollback Plan

1. Disable feature flag if available
2. Revert to legacy `FitsExportOptions` usage
3. Keep deprecated code until v2 release
4. Provide migration script for rollback if needed

---

## Phase 8: Database Schema and UI Configuration Management

### Overview

Extend the configuration system beyond appsettings.json to include:
- Database schema for persisting export configurations per deployment/site
- Configuration API endpoints for runtime updates
- UI components for managing export settings
- Configuration validation and preview

### Tasks

#### 8.1: Database Schema Updates

**File**: `src/HVO.DataModels/Migrations/AddExportConfigurationTables.cs` (new)

Create tables for persisting export configurations:

```sql
CREATE TABLE ExportConfigurations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ConfigurationName NVARCHAR(100) NOT NULL,
    Stage NVARCHAR(20) NOT NULL,  -- 'Raw' or 'Processed'
    Role NVARCHAR(20) NOT NULL,   -- 'Archive' or 'Delivery'
    Format NVARCHAR(20) NOT NULL, -- 'Jpeg', 'Png', 'Fits', etc.
    Quality INTEGER NOT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT 1,
    CreatedUtc DATETIME NOT NULL,
    ModifiedUtc DATETIME NOT NULL,
    UNIQUE(Stage, Role)
);

CREATE TABLE FitsExportConfigurationOptions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ExportConfigurationId INTEGER NOT NULL,
    BitDepth NVARCHAR(10) NOT NULL,      -- 'U8', 'U16', 'I16', etc.
    ImageFormat NVARCHAR(20) NOT NULL,    -- 'Mono', 'Rgb', 'Rgba', etc.
    UnsignedU16 BOOLEAN NOT NULL DEFAULT 1,
    Compression NVARCHAR(20) NOT NULL,    -- 'None', 'Rice', 'Gzip1', etc.
    WriteChecksum BOOLEAN NOT NULL DEFAULT 1,
    FOREIGN KEY (ExportConfigurationId) REFERENCES ExportConfigurations(Id) ON DELETE CASCADE
);

CREATE INDEX IX_ExportConfigurations_Stage_Role ON ExportConfigurations(Stage, Role);
CREATE INDEX IX_ExportConfigurations_IsActive ON ExportConfigurations(IsActive);
```

**Entity Models**:

```csharp
// src/HVO.DataModels/Models/ExportConfiguration.cs
public class ExportConfiguration
{
    public int Id { get; set; }
    public string ConfigurationName { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;  // "Raw" or "Processed"
    public string Role { get; set; } = string.Empty;   // "Archive" or "Delivery"
    public string Format { get; set; } = string.Empty; // "Jpeg", "Png", "Fits"
    public int Quality { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
    
    public FitsExportConfigurationOptions? FitsOptions { get; set; }
}

public class FitsExportConfigurationOptions
{
    public int Id { get; set; }
    public int ExportConfigurationId { get; set; }
    public string BitDepth { get; set; } = "U16";
    public string ImageFormat { get; set; } = "Mono";
    public bool UnsignedU16 { get; set; }
    public string Compression { get; set; } = "None";
    public bool WriteChecksum { get; set; }
    
    public ExportConfiguration? ExportConfiguration { get; set; }
}
```

**Tests**:
- `ExportConfigurationTests.CreateConfiguration_SetsPropertiesCorrectly()`
- `ExportConfigurationTests.UniqueConstraint_SameStageRole_ThrowsException()`
- `ExportConfigurationTests.FitsOptions_CascadeDelete_RemovesRelatedRows()`

#### 8.2: Configuration Repository

**File**: `src/HVO.DataModels/Repositories/ExportConfigurationRepository.cs` (new)

```csharp
public interface IExportConfigurationRepository
{
    Task<ExportConfiguration?> GetActiveConfigurationAsync(string stage, string role);
    Task<IReadOnlyList<ExportConfiguration>> GetAllConfigurationsAsync();
    Task<ExportConfiguration> CreateConfigurationAsync(ExportConfiguration config);
    Task<ExportConfiguration> UpdateConfigurationAsync(ExportConfiguration config);
    Task DeleteConfigurationAsync(int id);
    Task<bool> ActivateConfigurationAsync(int id);
    Task<FrameExportOptions> LoadIntoFrameExportOptionsAsync();
}

public class ExportConfigurationRepository : IExportConfigurationRepository
{
    private readonly SkyMonitorDbContext _context;
    private readonly ILogger<ExportConfigurationRepository> _logger;
    
    public async Task<FrameExportOptions> LoadIntoFrameExportOptionsAsync()
    {
        var configs = await GetAllConfigurationsAsync();
        var options = new FrameExportOptions();
        
        foreach (var config in configs.Where(c => c.IsActive))
        {
            var encoding = MapToImageEncodingSettings(config);
            
            var stageOptions = config.Stage.ToLowerInvariant() switch
            {
                "raw" => options.Raw,
                "processed" => options.Processed,
                _ => throw new InvalidOperationException($"Unknown stage: {config.Stage}")
            };
            
            switch (config.Role.ToLowerInvariant())
            {
                case "archive":
                    stageOptions.ArchiveEncoding = encoding;
                    break;
                case "delivery":
                    stageOptions.DeliveryEncoding = encoding;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown role: {config.Role}");
            }
        }
        
        return options;
    }
    
    private ImageEncodingSettings MapToImageEncodingSettings(ExportConfiguration config)
    {
        var format = Enum.Parse<ImageEncodingFormat>(config.Format, ignoreCase: true);
        var encoding = new ImageEncodingSettings(format, config.Quality);
        
        if (format == ImageEncodingFormat.Fits && config.FitsOptions != null)
        {
            encoding = encoding with
            {
                FitsOptions = new FitsEncodingOptions
                {
                    BitDepth = Enum.Parse<FitsBitDepth>(config.FitsOptions.BitDepth),
                    ImageFormat = Enum.Parse<FitsImageFormat>(config.FitsOptions.ImageFormat),
                    UnsignedU16 = config.FitsOptions.UnsignedU16,
                    Compression = Enum.Parse<FitsCompressionKind>(config.FitsOptions.Compression),
                    WriteChecksum = config.FitsOptions.WriteChecksum
                }
            };
        }
        
        return encoding;
    }
}
```

**Tests**:
- `ExportConfigurationRepositoryTests.GetActiveConfiguration_ReturnsCorrectConfig()`
- `ExportConfigurationRepositoryTests.LoadIntoFrameExportOptions_MapsAllFields()`
- `ExportConfigurationRepositoryTests.CreateConfiguration_AddsToDatabase()`
- `ExportConfigurationRepositoryTests.ActivateConfiguration_DeactivatesOthers()`

#### 8.3: Configuration Provider with Database Fallback

**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Services/HybridExportConfigurationProvider.cs` (new)

Combine appsettings.json with database configuration:

```csharp
public interface IHybridExportConfigurationProvider
{
    Task<FrameExportOptions> GetEffectiveConfigurationAsync();
    Task<ConfigurationSource> GetConfigurationSourceAsync();
}

public enum ConfigurationSource
{
    AppSettings,
    Database,
    Merged
}

public class HybridExportConfigurationProvider : IHybridExportConfigurationProvider
{
    private readonly IOptionsMonitor<FrameExportOptions> _appSettingsOptions;
    private readonly IExportConfigurationRepository _repository;
    private readonly ILogger<HybridExportConfigurationProvider> _logger;
    
    public async Task<FrameExportOptions> GetEffectiveConfigurationAsync()
    {
        try
        {
            // Try to load from database first
            var dbOptions = await _repository.LoadIntoFrameExportOptionsAsync();
            
            // If database has configurations, use them
            if (HasActiveConfigurations(dbOptions))
            {
                _logger.LogDebug("Using export configuration from database.");
                return dbOptions;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load export configuration from database, falling back to appsettings.json");
        }
        
        // Fallback to appsettings.json
        _logger.LogDebug("Using export configuration from appsettings.json.");
        return _appSettingsOptions.CurrentValue;
    }
    
    private bool HasActiveConfigurations(FrameExportOptions options)
    {
        return options.Raw.ArchiveEncoding != null || 
               options.Processed.ArchiveEncoding != null;
    }
}
```

**Tests**:
- `HybridExportConfigurationProviderTests.GetEffectiveConfiguration_DatabaseHasConfig_UsesDatabase()`
- `HybridExportConfigurationProviderTests.GetEffectiveConfiguration_DatabaseEmpty_UsesAppSettings()`
- `HybridExportConfigurationProviderTests.GetEffectiveConfiguration_DatabaseError_FallsBackToAppSettings()`

#### 8.4: Configuration API Endpoints

**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Controllers/v1_0/ExportConfigurationController.cs` (new)

REST API for managing export configurations:

```csharp
[ApiController]
[Route("api/v{version:apiVersion}/export-configuration")]
[ApiVersion("1.0")]
public class ExportConfigurationController : ControllerBase
{
    private readonly IExportConfigurationRepository _repository;
    private readonly IHybridExportConfigurationProvider _configProvider;
    private readonly ILogger<ExportConfigurationController> _logger;
    
    /// <summary>
    /// Get current effective export configuration.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ExportConfigurationResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentConfiguration()
    {
        var effective = await _configProvider.GetEffectiveConfigurationAsync();
        var source = await _configProvider.GetConfigurationSourceAsync();
        
        return Ok(new ExportConfigurationResponse
        {
            Source = source.ToString(),
            Raw = MapStageOptions(effective.Raw),
            Processed = MapStageOptions(effective.Processed)
        });
    }
    
    /// <summary>
    /// Get all saved configurations from database.
    /// </summary>
    [HttpGet("saved")]
    [ProducesResponseType(typeof(IEnumerable<ExportConfigurationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSavedConfigurations()
    {
        var configs = await _repository.GetAllConfigurationsAsync();
        return Ok(configs.Select(MapToDto));
    }
    
    /// <summary>
    /// Create a new export configuration.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ExportConfigurationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateConfiguration([FromBody] CreateExportConfigurationRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        var config = MapFromRequest(request);
        var created = await _repository.CreateConfigurationAsync(config);
        
        return CreatedAtAction(
            nameof(GetConfiguration), 
            new { id = created.Id }, 
            MapToDto(created));
    }
    
    /// <summary>
    /// Update an existing export configuration.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ExportConfigurationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateConfiguration(int id, [FromBody] UpdateExportConfigurationRequest request)
    {
        var existing = await _repository.GetByIdAsync(id);
        if (existing == null)
            return NotFound();
        
        ApplyUpdate(existing, request);
        var updated = await _repository.UpdateConfigurationAsync(existing);
        
        return Ok(MapToDto(updated));
    }
    
    /// <summary>
    /// Activate a configuration (deactivates others for same stage/role).
    /// </summary>
    [HttpPost("{id:int}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateConfiguration(int id)
    {
        var activated = await _repository.ActivateConfigurationAsync(id);
        if (!activated)
            return NotFound();
        
        return NoContent();
    }
    
    /// <summary>
    /// Delete a configuration.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConfiguration(int id)
    {
        await _repository.DeleteConfigurationAsync(id);
        return NoContent();
    }
    
    /// <summary>
    /// Preview what a configuration would produce (validates encoding).
    /// </summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ConfigurationPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreviewConfiguration([FromBody] ExportConfigurationDto config)
    {
        try
        {
            var encoding = MapToImageEncodingSettings(config);
            var validation = ValidateEncoding(encoding);
            
            return Ok(new ConfigurationPreviewResponse
            {
                IsValid = validation.IsValid,
                Warnings = validation.Warnings,
                EstimatedFileSize = EstimateFileSize(encoding),
                ContentType = ImageEncodingUtilities.ToContentType(encoding.Format),
                FileExtension = ImageEncodingUtilities.ToFileExtension(encoding.Format)
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

// DTOs
public record ExportConfigurationResponse
{
    public string Source { get; init; } = string.Empty;
    public StageConfigurationDto? Raw { get; init; }
    public StageConfigurationDto? Processed { get; init; }
}

public record StageConfigurationDto
{
    public EncodingSettingsDto? ArchiveEncoding { get; init; }
    public EncodingSettingsDto? DeliveryEncoding { get; init; }
    public string PayloadScope { get; init; } = string.Empty;
}

public record EncodingSettingsDto
{
    public string Format { get; init; } = string.Empty;
    public int Quality { get; init; }
    public FitsOptionsDto? FitsOptions { get; init; }
}

public record FitsOptionsDto
{
    public string BitDepth { get; init; } = "U16";
    public string ImageFormat { get; init; } = "Mono";
    public bool UnsignedU16 { get; init; }
    public string Compression { get; init; } = "None";
    public bool WriteChecksum { get; init; }
}

public record CreateExportConfigurationRequest
{
    [Required]
    public string ConfigurationName { get; init; } = string.Empty;
    
    [Required]
    [RegularExpression("^(Raw|Processed)$")]
    public string Stage { get; init; } = string.Empty;
    
    [Required]
    [RegularExpression("^(Archive|Delivery)$")]
    public string Role { get; init; } = string.Empty;
    
    [Required]
    public EncodingSettingsDto Encoding { get; init; } = new();
}

public record ConfigurationPreviewResponse
{
    public bool IsValid { get; init; }
    public List<string> Warnings { get; init; } = new();
    public long EstimatedFileSize { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
}
```

**Tests**:
- `ExportConfigurationControllerTests.GetCurrentConfiguration_ReturnsEffectiveConfig()`
- `ExportConfigurationControllerTests.CreateConfiguration_ValidRequest_ReturnsCreated()`
- `ExportConfigurationControllerTests.UpdateConfiguration_ExistingId_ReturnsUpdated()`
- `ExportConfigurationControllerTests.ActivateConfiguration_DeactivatesOthers()`
- `ExportConfigurationControllerTests.PreviewConfiguration_InvalidFormat_ReturnsBadRequest()`

#### 8.5: UI Configuration Components

**File**: `src/HVO.WebSite.Playground/Components/Pages/ExportConfiguration.razor` (new)

Blazor component for managing export settings:

```razor
@page "/configuration/export"
@using HVO.SkyMonitorV5.RPi.Controllers.v1_0
@inject HttpClient Http
@inject ILogger<ExportConfiguration> Logger

<PageTitle>Export Configuration</PageTitle>

<h1>Frame Export Configuration</h1>

<div class="configuration-container">
    <!-- Current Active Configuration Display -->
    <div class="card mb-4">
        <div class="card-header">
            <h3>Current Active Configuration</h3>
            <span class="badge bg-info">Source: @currentConfig?.Source</span>
        </div>
        <div class="card-body">
            @if (currentConfig != null)
            {
                <div class="row">
                    <div class="col-md-6">
                        <h4>Raw Frames</h4>
                        <ConfigurationDisplay Stage="Raw" Config="@currentConfig.Raw" />
                    </div>
                    <div class="col-md-6">
                        <h4>Processed Frames</h4>
                        <ConfigurationDisplay Stage="Processed" Config="@currentConfig.Processed" />
                    </div>
                </div>
            }
        </div>
    </div>

    <!-- Configuration Editor -->
    <div class="card mb-4">
        <div class="card-header">
            <h3>Create/Edit Configuration</h3>
        </div>
        <div class="card-body">
            <EditForm Model="@editModel" OnValidSubmit="@HandleSubmit">
                <DataAnnotationsValidator />
                <ValidationSummary />
                
                <div class="mb-3">
                    <label class="form-label">Configuration Name</label>
                    <InputText class="form-control" @bind-Value="editModel.ConfigurationName" />
                </div>
                
                <div class="mb-3">
                    <label class="form-label">Stage</label>
                    <InputSelect class="form-select" @bind-Value="editModel.Stage">
                        <option value="Raw">Raw</option>
                        <option value="Processed">Processed</option>
                    </InputSelect>
                </div>
                
                <div class="mb-3">
                    <label class="form-label">Role</label>
                    <InputSelect class="form-select" @bind-Value="editModel.Role">
                        <option value="Archive">Archive</option>
                        <option value="Delivery">Delivery</option>
                    </InputSelect>
                </div>
                
                <div class="mb-3">
                    <label class="form-label">Format</label>
                    <InputSelect class="form-select" @bind-Value="editModel.Encoding.Format" 
                                 @bind-Value:after="OnFormatChanged">
                        <option value="Jpeg">JPEG</option>
                        <option value="Png">PNG</option>
                        <option value="Fits">FITS</option>
                        <option value="Tiff" disabled>TIFF (Coming Soon)</option>
                    </InputSelect>
                </div>
                
                <div class="mb-3">
                    <label class="form-label">Quality (0-100)</label>
                    <InputNumber class="form-control" @bind-Value="editModel.Encoding.Quality" />
                </div>
                
                @if (editModel.Encoding.Format == "Fits")
                {
                    <div class="card mb-3">
                        <div class="card-header">FITS Options</div>
                        <div class="card-body">
                            <div class="mb-3">
                                <label class="form-label">Bit Depth</label>
                                <InputSelect class="form-select" @bind-Value="editModel.Encoding.FitsOptions!.BitDepth">
                                    <option value="U8">8-bit Unsigned</option>
                                    <option value="U16">16-bit Unsigned</option>
                                    <option value="I16">16-bit Signed</option>
                                    <option value="I32">32-bit Signed</option>
                                    <option value="F32">32-bit Float</option>
                                    <option value="F64">64-bit Float</option>
                                </InputSelect>
                            </div>
                            
                            <div class="mb-3">
                                <label class="form-label">Image Format</label>
                                <InputSelect class="form-select" @bind-Value="editModel.Encoding.FitsOptions.ImageFormat">
                                    <option value="Mono">Monochrome</option>
                                    <option value="Rgb">RGB Color (Coming Soon)</option>
                                    <option value="Rgba">RGBA Color (Coming Soon)</option>
                                    <option value="BayerMosaic">Bayer Mosaic (Raw Only)</option>
                                </InputSelect>
                            </div>
                            
                            <div class="mb-3">
                                <label class="form-label">Compression</label>
                                <InputSelect class="form-select" @bind-Value="editModel.Encoding.FitsOptions.Compression">
                                    <option value="None">None</option>
                                    <option value="Rice">Rice (Lossless)</option>
                                    <option value="Gzip1">GZIP Level 1</option>
                                    <option value="Gzip2">GZIP Level 2</option>
                                    <option value="HCompress">HCompress (Lossy)</option>
                                </InputSelect>
                            </div>
                            
                            <div class="form-check mb-3">
                                <InputCheckbox class="form-check-input" @bind-Value="editModel.Encoding.FitsOptions.UnsignedU16" />
                                <label class="form-check-label">Unsigned U16 Scaling</label>
                            </div>
                            
                            <div class="form-check mb-3">
                                <InputCheckbox class="form-check-input" @bind-Value="editModel.Encoding.FitsOptions.WriteChecksum" />
                                <label class="form-check-label">Write Checksum</label>
                            </div>
                        </div>
                    </div>
                }
                
                <div class="d-flex gap-2">
                    <button type="button" class="btn btn-secondary" @onclick="PreviewConfiguration">
                        Preview
                    </button>
                    <button type="submit" class="btn btn-primary">
                        Save Configuration
                    </button>
                </div>
            </EditForm>
            
            @if (preview != null)
            {
                <div class="alert alert-info mt-3">
                    <h5>Preview</h5>
                    <dl>
                        <dt>Content Type:</dt>
                        <dd>@preview.ContentType</dd>
                        <dt>File Extension:</dt>
                        <dd>.@preview.FileExtension</dd>
                        <dt>Estimated Size:</dt>
                        <dd>@FormatBytes(preview.EstimatedFileSize)</dd>
                    </dl>
                    @if (preview.Warnings.Any())
                    {
                        <div class="alert alert-warning">
                            <strong>Warnings:</strong>
                            <ul>
                                @foreach (var warning in preview.Warnings)
                                {
                                    <li>@warning</li>
                                }
                            </ul>
                        </div>
                    }
                </div>
            }
        </div>
    </div>

    <!-- Saved Configurations List -->
    <div class="card">
        <div class="card-header">
            <h3>Saved Configurations</h3>
        </div>
        <div class="card-body">
            <table class="table">
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Stage</th>
                        <th>Role</th>
                        <th>Format</th>
                        <th>Quality</th>
                        <th>Status</th>
                        <th>Actions</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var config in savedConfigurations)
                    {
                        <tr class="@(config.IsActive ? "table-active" : "")">
                            <td>@config.ConfigurationName</td>
                            <td>@config.Stage</td>
                            <td>@config.Role</td>
                            <td>@config.Format</td>
                            <td>@config.Quality</td>
                            <td>
                                @if (config.IsActive)
                                {
                                    <span class="badge bg-success">Active</span>
                                }
                            </td>
                            <td>
                                @if (!config.IsActive)
                                {
                                    <button class="btn btn-sm btn-primary" @onclick="() => ActivateConfig(config.Id)">
                                        Activate
                                    </button>
                                }
                                <button class="btn btn-sm btn-danger" @onclick="() => DeleteConfig(config.Id)">
                                    Delete
                                </button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>

@code {
    private ExportConfigurationResponse? currentConfig;
    private List<ExportConfigurationDto> savedConfigurations = new();
    private CreateExportConfigurationRequest editModel = new();
    private ConfigurationPreviewResponse? preview;
    
    protected override async Task OnInitializedAsync()
    {
        await LoadCurrentConfiguration();
        await LoadSavedConfigurations();
        InitializeEditModel();
    }
    
    private async Task LoadCurrentConfiguration()
    {
        currentConfig = await Http.GetFromJsonAsync<ExportConfigurationResponse>("/api/v1.0/export-configuration");
    }
    
    private async Task LoadSavedConfigurations()
    {
        savedConfigurations = await Http.GetFromJsonAsync<List<ExportConfigurationDto>>("/api/v1.0/export-configuration/saved") 
            ?? new();
    }
    
    private void InitializeEditModel()
    {
        editModel = new CreateExportConfigurationRequest
        {
            ConfigurationName = "New Configuration",
            Stage = "Processed",
            Role = "Archive",
            Encoding = new EncodingSettingsDto
            {
                Format = "Jpeg",
                Quality = 95
            }
        };
    }
    
    private void OnFormatChanged()
    {
        if (editModel.Encoding.Format == "Fits")
        {
            editModel.Encoding = editModel.Encoding with
            {
                FitsOptions = new FitsOptionsDto
                {
                    BitDepth = "U16",
                    ImageFormat = "Mono",
                    UnsignedU16 = true,
                    Compression = "Rice",
                    WriteChecksum = true
                }
            };
        }
        else
        {
            editModel.Encoding = editModel.Encoding with { FitsOptions = null };
        }
    }
    
    private async Task PreviewConfiguration()
    {
        var response = await Http.PostAsJsonAsync("/api/v1.0/export-configuration/preview", editModel.Encoding);
        preview = await response.Content.ReadFromJsonAsync<ConfigurationPreviewResponse>();
    }
    
    private async Task HandleSubmit()
    {
        var response = await Http.PostAsJsonAsync("/api/v1.0/export-configuration", editModel);
        if (response.IsSuccessStatusCode)
        {
            await LoadSavedConfigurations();
            InitializeEditModel();
            preview = null;
        }
    }
    
    private async Task ActivateConfig(int id)
    {
        await Http.PostAsync($"/api/v1.0/export-configuration/{id}/activate", null);
        await LoadCurrentConfiguration();
        await LoadSavedConfigurations();
    }
    
    private async Task DeleteConfig(int id)
    {
        await Http.DeleteAsync($"/api/v1.0/export-configuration/{id}");
        await LoadSavedConfigurations();
    }
    
    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
```

**Shared Component** - `ConfigurationDisplay.razor`:

```razor
@using HVO.SkyMonitorV5.RPi.Controllers.v1_0

<div class="configuration-display">
    @if (Config?.ArchiveEncoding != null)
    {
        <div class="mb-3">
            <h5>Archive</h5>
            <EncodingDisplay Encoding="@Config.ArchiveEncoding" />
        </div>
    }
    
    @if (Config?.DeliveryEncoding != null)
    {
        <div class="mb-3">
            <h5>Delivery</h5>
            <EncodingDisplay Encoding="@Config.DeliveryEncoding" />
        </div>
    }
    
    @if (Config != null)
    {
        <div class="mb-3">
            <strong>Payload Scope:</strong> @Config.PayloadScope
        </div>
    }
</div>

@code {
    [Parameter]
    public string Stage { get; set; } = string.Empty;
    
    [Parameter]
    public StageConfigurationDto? Config { get; set; }
}
```

**Tests**:
- `ExportConfigurationPageTests.LoadConfigurations_DisplaysCurrent()`
- `ExportConfigurationPageTests.CreateConfiguration_CallsApi()`
- `ExportConfigurationPageTests.FormatChange_UpdatesFitsOptions()`
- `ExportConfigurationPageTests.PreviewConfiguration_DisplaysEstimate()`

#### 8.6: Configuration Validation

**File**: `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Services/ExportConfigurationValidator.cs` (new)

```csharp
public interface IExportConfigurationValidator
{
    ValidationResult Validate(ImageEncodingSettings encoding);
    long EstimateFileSize(ImageEncodingSettings encoding, int width = 1920, int height = 1080);
}

public class ExportConfigurationValidator : IExportConfigurationValidator
{
    public ValidationResult Validate(ImageEncodingSettings encoding)
    {
        var warnings = new List<string>();
        
        // Format-specific validation
        switch (encoding.Format)
        {
            case ImageEncodingFormat.Fits:
                if (encoding.FitsOptions == null)
                    warnings.Add("FITS format requires FitsOptions to be specified. Defaults will be used.");
                else
                {
                    if (encoding.FitsOptions.ImageFormat == FitsImageFormat.Rgb)
                        warnings.Add("RGB FITS format is not yet fully implemented.");
                    if (encoding.FitsOptions.Compression == FitsCompressionKind.HCompress)
                        warnings.Add("HCompress is lossy and may reduce scientific accuracy.");
                }
                break;
                
            case ImageEncodingFormat.Jpeg:
                if (encoding.Quality < 80)
                    warnings.Add("JPEG quality below 80 may introduce visible compression artifacts.");
                if (encoding.Quality > 95)
                    warnings.Add("JPEG quality above 95 provides diminishing returns in file size vs quality.");
                break;
                
            case ImageEncodingFormat.Png:
                // PNG quality doesn't affect visual quality (lossless), only compression
                break;
        }
        
        return new ValidationResult
        {
            IsValid = true,
            Warnings = warnings
        };
    }
    
    public long EstimateFileSize(ImageEncodingSettings encoding, int width = 1920, int height = 1080)
    {
        long pixels = width * height;
        
        return encoding.Format switch
        {
            ImageEncodingFormat.Fits => EstimateFitsSize(pixels, encoding.FitsOptions),
            ImageEncodingFormat.Jpeg => EstimateJpegSize(pixels, encoding.Quality),
            ImageEncodingFormat.Png => EstimatePngSize(pixels),
            _ => pixels * 3 // Conservative estimate
        };
    }
    
    private long EstimateFitsSize(long pixels, FitsEncodingOptions? options)
    {
        var bytesPerPixel = (options?.BitDepth ?? FitsBitDepth.U16) switch
        {
            FitsBitDepth.U8 => 1,
            FitsBitDepth.U16 or FitsBitDepth.I16 => 2,
            FitsBitDepth.I32 or FitsBitDepth.F32 => 4,
            FitsBitDepth.F64 => 8,
            _ => 2
        };
        
        var baseSize = pixels * bytesPerPixel;
        var compressionRatio = (options?.Compression ?? FitsCompressionKind.None) switch
        {
            FitsCompressionKind.Rice => 0.5,
            FitsCompressionKind.Gzip1 or FitsCompressionKind.Gzip2 => 0.6,
            FitsCompressionKind.HCompress => 0.3,
            _ => 1.0
        };
        
        return (long)(baseSize * compressionRatio) + 2880; // FITS header
    }
    
    private long EstimateJpegSize(long pixels, int quality)
    {
        // Empirical JPEG size estimation
        var bytesPerPixel = quality switch
        {
            >= 95 => 1.5,
            >= 90 => 1.0,
            >= 80 => 0.6,
            >= 70 => 0.4,
            _ => 0.2
        };
        
        return (long)(pixels * bytesPerPixel);
    }
    
    private long EstimatePngSize(long pixels)
    {
        // PNG is lossless but compressed, typically 50-70% of raw size
        return (long)(pixels * 3 * 0.6);
    }
}

public record ValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Warnings { get; init; } = new();
}
```

**Tests**:
- `ExportConfigurationValidatorTests.Validate_FitsWithoutOptions_ReturnsWarning()`
- `ExportConfigurationValidatorTests.Validate_LowQualityJpeg_ReturnsWarning()`
- `ExportConfigurationValidatorTests.EstimateFileSize_Fits_ReturnsReasonableSize()`
- `ExportConfigurationValidatorTests.EstimateFileSize_WithCompression_ReducesSize()`

---

## Future Enhancements (Post-Phase 8)

### Planned Format Support

1. **TIFF** (Phase 9)
   - 16-bit depth support
   - Multiple compression options
   - Popular for publications

2. **XISF** (Phase 10)
   - PixInsight native format
   - Embedded metadata
   - Efficient compression

3. **RGB/Color FITS** (Phase 11)
   - 3-plane FITS cubes
   - Color frame processing pipeline
   - Debayering support

### Architecture Improvements

- Multi-payload envelopes for different archive/delivery formats
- Lazy encoding (encode on-demand vs pre-encode)
- Format negotiation via Accept header
- Streaming large files
- Configuration templates/presets
- Import/export configuration as JSON
- Configuration history and rollback

---

## Appendix: Configuration Examples

### Minimal Configuration (Defaults)

```json
{
  "FrameExport": {
    "Raw": { "Enabled": true },
    "Processed": { "Enabled": true }
  }
}
```

Result:
- Raw: FITS U16 @ 100%
- Processed: JPEG @ 95%

### Scientific Archive

```json
{
  "FrameExport": {
    "Raw": {
      "ArchiveEncoding": {
        "Format": "Fits",
        "FitsOptions": {
          "BitDepth": "U16",
          "Compression": "Rice"
        }
      }
    },
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Fits",
        "FitsOptions": {
          "BitDepth": "F32",
          "ImageFormat": "Rgb"
        }
      }
    }
  }
}
```

### Web Observatory

```json
{
  "FrameExport": {
    "Raw": {
      "ArchiveEncoding": {
        "Format": "Fits",
        "FitsOptions": { "BitDepth": "U16" }
      },
      "DeliveryEncoding": {
        "Format": "Jpeg",
        "Quality": 75
      },
      "PayloadScope": "ArchiveAndDelivery"
    },
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Png",
        "Quality": 100
      },
      "DeliveryEncoding": {
        "Format": "Jpeg",
        "Quality": 85
      },
      "PayloadScope": "ArchiveAndDelivery"
    }
  }
}
```

---

## Sign-Off

**Ready to Implement**: ✅

This plan provides:
- Clear phases with concrete tasks
- Comprehensive test coverage
- Migration path for existing users
- Documentation updates
- Risk mitigation strategies
- Realistic timeline

**Next Step**: Proceed with Phase 1 implementation.
