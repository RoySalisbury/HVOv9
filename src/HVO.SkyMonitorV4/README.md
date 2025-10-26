# HVO.SkyMonitorV4 - All-Sky Camera (DEPRECATED)

[![SkyMonitor V4 CI](https://github.com/RoySalisbury/HVOv9/actions/workflows/skymonitor-v4.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/skymonitor-v4.yml)

> **⚠️ DEPRECATED - Use [HVO.SkyMonitorV5](../HVO.SkyMonitorV5/README.md) Instead**
>
> SkyMonitorV4 is the legacy all-sky camera monitoring system. It has been superseded by **SkyMonitorV5**, which offers:
> - **10x faster processing** (SkiaSharp vs. ImageSharp)
> - **Better star detection** (SNR-based algorithm)
> - **Improved cloud analysis** (dual-method coverage calculation)
> - **Modern architecture** (Blazor Server, WASM viewer)
> - **Active development** (V4 is maintenance-only)

## 📦 Legacy System Overview

SkyMonitorV4 was the fourth iteration of the all-sky camera system, providing:
- All-sky imaging with ZWO ASI cameras
- Basic star detection
- Cloud coverage estimation
- Historical data storage

## 🔄 Migration to V5

**Migration Guide**: See [SkyMonitor V5 Migration Guide](../../docs/skymonitor-v5-json-migration-guide.md)

### Key Differences

| Feature | V4 (Legacy) | V5 (Current) |
|---------|-------------|--------------|
| Image Processing | ImageSharp | SkiaSharp (hardware accelerated) |
| Star Detection | Simple threshold | SNR-based with FWHM validation |
| Cloud Analysis | Star count only | Dual-method (stars + pixels) |
| Processing Time (RPi5) | ~35s | ~3.5s |
| UI Framework | Razor Pages | Blazor Server + WASM |
| Performance Tests | Manual | BenchmarkDotNet + Stress tests |
| Docker Support | Basic | Full orchestration |

### Migration Steps

1. **Export V4 Historical Data** (if needed):
   ```bash
   cd src/HVO.SkyMonitorV4/HVO.SkyMonitorV4.RPi
   dotnet run -- --export-data /data/v4-export
   ```

2. **Install V5**:
   ```bash
   cd src/HVO.SkyMonitorV5
   docker-compose up -d
   ```

3. **Import V4 Data into V5** (optional):
   ```bash
   # V5 can read V4 FITS files directly
   # Update appsettings.json to point to V4 archive path
   ```

4. **Update Integrations**:
   - Update API endpoints from `/api/v4/sky` to `/api/v1/sky`
   - Update WebSocket connections (V5 uses different event structure)
   - Update database queries (V5 uses different schema)

## 🛠️ Maintenance Status

- **Bug Fixes**: Security and critical bugs only
- **New Features**: ❌ None planned
- **Support**: Until December 2026 (then archive)
- **Recommended Action**: Migrate to V5 as soon as possible

## 📁 Projects (Legacy)

### HVO.SkyMonitorV4.RPi
Legacy Razor Pages application:
- ZWO camera control
- ImageSharp-based processing
- SQLite data storage

### HVO.SkyMonitorV4.CLI
Command-line utility for V4:
- Batch image processing
- Data export tools

## 🔗 Dependencies (Frozen)

Dependencies are frozen at last stable versions:
- `SixLabors.ImageSharp` 3.x (no longer updated)
- `HVO.ZWOOptical.ASISDK` (shared with V5)
- `HVO.Astronomy.CFITSIO` (shared with V5)

## 📖 Related Documentation

- **Current System**: [HVO.SkyMonitorV5](../HVO.SkyMonitorV5/README.md)
- **Migration Guide**: [V4 to V5 Migration](../../docs/skymonitor-v5-json-migration-guide.md)
- **V5 Operations**: [SkyMonitor V5 Runbook](../../docs/skymonitor-v5-operations-runbook.md)

## ⚠️ End-of-Life Timeline

- **October 2024**: V5 released, V4 enters maintenance mode
- **October 2025**: V4 maintenance-only (current status)
- **December 2026**: V4 end-of-life, archived
- **Action Required**: Migrate to V5 before December 2026
