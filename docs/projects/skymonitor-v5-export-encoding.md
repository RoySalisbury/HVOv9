# SkyMonitor V5 Export Encoding Configuration

## Overview

The frame export system supports configurable encoding settings for processed frame exports, allowing control over image format (JPEG/PNG) and quality per export role (Archive vs Delivery).

## Current Implementation (v1 - Single Payload Architecture)

### Configuration

Encoding settings are configured per stage in `FrameExportOptions`:

```json
{
  "FrameExport": {
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Jpeg",  // or "Png"
        "Quality": 95      // 0-100
      },
      "DeliveryEncoding": {
        // Reserved for future use - currently not applied
        "Format": "Jpeg",
        "Quality": 80
      },
      "PayloadScope": "ArchiveOnly"  // or "DeliveryOnly", "ArchiveAndDelivery"
    }
  }
}
```

### Defaults

- **ArchiveEncoding**: JPEG @ 95% quality (high-quality long-term storage)
- **DeliveryEncoding**: `null` (not yet supported; see Future Enhancements)
- **PayloadScope**: `ArchiveOnly` for processed exports

### Behavior

1. **Single Payload Creation**: Publisher creates ONE envelope with ONE payload per frame
2. **Encoding Applied**: Uses `ArchiveEncoding` settings for all processed exports
3. **Distribution**: Same payload written to all configured export targets (archive and/or delivery directories)

### FITS vs JPG/PNG Decision Logic

For **RAW** frames:
- FITS format when `FitsExport.EnableForRaw` is true
- Fallback to raw linear payloads or PNG @ 95% when FITS disabled

For **PROCESSED** frames (NEVER FITS):
- Always use `ArchiveEncoding` settings (JPEG @ 95% by default)
- `ProcessedFrameEncodingContext.UserInterface` ensures no FITS encoding
- Custom encoding parameter allows override (used by publisher for ArchiveEncoding)

## Architecture Components

### FrameExportStageOptions

**Location**: `HVO.SkyMonitorV5.RPi/Options/FrameExportOptions.cs`

**Properties**:
```csharp
public ImageEncodingSettings ArchiveEncoding { get; set; } = new(ImageEncodingFormat.Jpeg, 95);
public ImageEncodingSettings? DeliveryEncoding { get; set; }  // Reserved for v2
```

**Normalization**:
- Ensures `ArchiveEncoding` is never null (defaults to JPEG @ 95%)
- `DeliveryEncoding` remains null until v2 implementation

### IProcessedFrameEncoder

**Location**: `HVO.SkyMonitorV5.RPi/Services/IProcessedFrameEncoder.cs`

**Interface**:
```csharp
ProcessedFrameDelivery Encode(
    ProcessedFrame frame, 
    ProcessedFrameEncodingContext context = ProcessedFrameEncodingContext.UserInterface,
    ImageEncodingSettings? customEncoding = null);
```

**Parameters**:
- `context`: `UserInterface` (never FITS) or `Export` (FITS when enabled)
- `customEncoding`: Override frame's encoding with custom settings

**Behavior**:
- UI context: Always returns JPG/PNG (never FITS)
- Export context: Returns FITS when enabled, otherwise JPG/PNG
- Custom encoding: Overrides frame's format/quality when provided

### FrameExportPublisher

**Location**: `HVO.SkyMonitorV5.RPi/Exports/FrameExportPublisher.cs`

**PublishProcessedFrame Logic**:
1. Get `ArchiveEncoding` from export options
2. Encode frame using `UserInterface` context + `ArchiveEncoding` override
3. Create single envelope with encoded payload
4. Dispatch to sinks (filesystem, S3)

## Future Enhancements (v2 - Multi-Payload Architecture)

### Planned Capabilities

**Goal**: Support different encodings per export role when `PayloadScope` is `ArchiveAndDelivery`.

**Example Use Case**:
- Archive: PNG @ 100% (lossless, archival quality)
- Delivery: JPEG @ 75% (smaller, web-friendly distribution)

### Required Changes

1. **Envelope Enhancement**:
   ```csharp
   public record FrameExportEnvelope(
       Guid FrameId,
       FrameExportStage Stage,
       FrameExportPayloadRole Role,  // NEW: Archive or Delivery
       FrameExportMetadata Metadata,
       ReadOnlyMemory<byte> Payload,
       string ContentType,
       string? FileExtension);
   ```

2. **Publisher Logic**:
   - When `ArchiveAndDelivery` with different encodings:
     - Create TWO envelopes with DIFFERENT payloads
     - Each envelope tagged with its role
   - When encodings match or single role:
     - Use current single-envelope approach

3. **Sink Logic**:
   - Sinks check envelope's `Role` property
   - Write only to matching target directory (archive vs delivery)
   - Remove role enumeration loop (envelope is pre-tagged)

4. **Dispatcher Logic**:
   - Queue accepts multiple envelopes per frame
   - Deduplicates based on `(FrameId, Stage, Role)` tuple

### Migration Path

**Phase 1 (Current)**: Single-payload foundation
- ✅ Configuration schema supports both roles
- ✅ `ArchiveEncoding` fully implemented and tested
- ✅ `DeliveryEncoding` available but documented as reserved

**Phase 2 (Future)**: Multi-payload implementation
- Add `Role` to envelope structure
- Update publisher to create role-specific envelopes
- Refactor sinks to use envelope role instead of enumerating
- Enable `DeliveryEncoding` and test dual-payload scenarios

### Backward Compatibility

When upgrading to v2:
- Existing configurations with only `ArchiveEncoding` continue working unchanged
- `DeliveryEncoding` remains optional - when null, uses `ArchiveEncoding`
- Single-role scopes (`ArchiveOnly`, `DeliveryOnly`) remain single-payload optimized

## Testing

### Unit Tests

**ProcessedFrameEncoderTests**:
- `Encode_WithCustomEncoding_OverridesFrameSettings`: Verifies custom encoding override
- Context-specific encoding tests (UI vs Export)
- Default parameter behavior

**FrameExportPublisherTests**:
- Verified publisher uses `ArchiveEncoding` from options
- Mock setup ensures custom encoding parameter passed correctly

**FrameExportOptionsTests**:
- Default PayloadScope validation (ArchiveOnly for processed)
- Normalization ensures encoding defaults

### Integration Tests

Golden fixtures and sink tests verify end-to-end encoding:
- Archive directory contains JPEG @ 95% by default
- Delivery directory (when enabled) contains same payload currently
- Content-Type and file extensions match encoding settings

## Configuration Examples

### High-Quality Archive Only (Default)

```json
{
  "FrameExport": {
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Jpeg",
        "Quality": 95
      },
      "PayloadScope": "ArchiveOnly"
    }
  }
}
```

### PNG Lossless Archives

```json
{
  "FrameExport": {
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Png",
        "Quality": 100
      },
      "PayloadScope": "ArchiveOnly"
    }
  }
}
```

### Both Archive and Delivery (Same Encoding)

```json
{
  "FrameExport": {
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Jpeg",
        "Quality": 90
      },
      "PayloadScope": "ArchiveAndDelivery"
      // DeliveryEncoding not set = uses ArchiveEncoding
    }
  }
}
```

### Reserved: Different Encoding Per Role (v2)

```json
{
  "FrameExport": {
    "Processed": {
      "ArchiveEncoding": {
        "Format": "Png",
        "Quality": 100
      },
      "DeliveryEncoding": {
        "Format": "Jpeg",
        "Quality": 75
      },
      "PayloadScope": "ArchiveAndDelivery"
    }
  }
}
```

**Note**: In v1, both archive and delivery will receive PNG @ 100%. The `DeliveryEncoding` will be honored in v2.

## Related Documentation

- [SkyMonitor V5 Operations Runbook](../skymonitor-v5-operations-runbook.md)
- [Frame Export Options](../../src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Options/FrameExportOptions.cs)
- [FITS Export Configuration](../../src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Options/FitsExportOptions.cs)
