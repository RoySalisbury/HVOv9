# HVOv9 Source Code

This directory contains all source code for the Hualapai Valley Observatory v9 software suite, organized into logical domain-based folders.

## 📁 Project Structure

### Core Libraries
Located at the root of `src/` - shared by all projects:

- **[HVO/](HVO/)** - Core library with `Result<T>` pattern, IoT abstractions, and shared utilities
- **[HVO.DataModels/](HVO.DataModels/)** - Entity Framework Core models, contexts, and repositories
- **[HVO.SourceGenerators/](HVO.SourceGenerators/)** - Build-time code generation for reducing boilerplate
- **[HVO.WebSite.Themes/](HVO.WebSite.Themes/)** - Shared Blazor UI themes and components

### Domain Projects
Each domain has its own folder with related projects and domain-specific solution file:

#### 🔭 Astronomy Domain
**[HVO.Astronomy/](HVO.Astronomy/)**
- `HVO.Astronomy.CFITSIO` - FITS file I/O library
- `HVO.Astronomy.CFITSIO.NativeAssets` - Platform-specific native binaries
- `HVO.Astronomy.CFITSIO.Tests` - Unit tests
- Solution: `HVO.Astronomy.sln`

#### 🔌 IoT Devices Domain
**[HVO.Iot/](HVO.Iot/)**
- `HVO.Iot.Devices` - Hardware device implementations (GPIO, sensors, relays)
- `HVO.Iot.Devices.Tests` - Hardware abstraction tests
- Solution: `HVO.Iot.sln`
- 📦 *Future NuGet package candidate*

#### 📷 NINA Integration
**[HVO.NINA/](HVO.NINA/)**
- `HVO.NinaClient` - NINA (Nighttime Imaging 'N' Astronomy) API client
- Solution: `HVO.NINA.sln`

#### 🏛️ RoofController V4
**[HVO.RoofControllerV4/](HVO.RoofControllerV4/)**
- `HVO.RoofControllerV4.Common` - Shared DTOs and models
- `HVO.RoofControllerV4.RPi` - Raspberry Pi controller (Blazor Server)
- `HVO.RoofControllerV4.RPi.Tests` - Integration and unit tests
- Solution: `HVO.RoofControllerV4.sln`
- Docker: `docker-compose.yml`

#### 📱 iOS/MAUI Applications
**[HVO.iOS/](HVO.iOS/)**
- `HVO.RoofControllerV4.iPad` - iPad/iOS roof controller app
- Solution: `HVO.iOS.sln`
- Platform: macOS required for building

#### 🌃 SkyMonitor V4 (Legacy)
**[HVO.SkyMonitorV4/](HVO.SkyMonitorV4/)**
- `HVO.SkyMonitorV4.RPi` - Legacy V4 implementation
- `HVO.SkyMonitorV4.CLI` - Command-line tools
- Solution: `HVO.SkyMonitorV4.sln`

#### 🌌 SkyMonitor V5 (Current)
**[HVO.SkyMonitorV5/](HVO.SkyMonitorV5/)**
- `HVO.SkyMonitorV5.Data` - Data models and EF context
- `HVO.SkyMonitorV5.RPi` - Current implementation with WASM viewer
- `HVO.SkyMonitorV5.RPi.Tests` - Unit and integration tests
- `HVO.SkyMonitorV5.RPi.Benchmarks` - Performance benchmarks
- `HVO.SkyMonitorV5.RPi.Stress` - Load testing
- Solution: `HVO.SkyMonitorV5.sln`
- Docker: `docker-compose.yml` (includes MinIO)

#### 🌐 WebSite Domain
**[HVO.WebSite/](HVO.WebSite/)**
- `HVO.WebSite.v9` - Main observatory website (Blazor Server)
- `HVO.WebSite.Playground` - Development/testing site
- `HVO.WebSite.Playground.Tests` - Website tests
- Solution: `HVO.WebSite.sln`
- Docker: `docker-compose.yml`

#### 📸 ZWO Optical SDK
**[HVO.ZWOOptical/](HVO.ZWOOptical/)**
- `HVO.ZWOOptical.ASISDK` - ZWO ASI camera SDK wrapper
- Solution: `HVO.ZWOOptical.sln`

#### 🎮 Playground & Utilities
**[HVO.Playground/](HVO.Playground/)**
- `HVO.Playground.CLI` - General-purpose CLI for experimentation
- `HVO.GpioTestApp` - GPIO hardware testing utility
- Solution: `HVO.Playground.sln`

## 🏗️ Building

### Build Everything
```bash
dotnet build HVOv9.sln
```

### Build Specific Domain
```bash
dotnet build HVO.SkyMonitorV5/HVO.SkyMonitorV5.sln
dotnet build HVO.RoofControllerV4/HVO.RoofControllerV4.sln
# etc.
```

### Build Configuration
All projects use centralized package management:
- `Directory.Build.props` - Common MSBuild properties
- `Directory.Packages.props` - Central Package Version Management (CPVM)
- `global.json` - .NET SDK version pinning (9.0.304)
- `NuGet.config` - Package sources including local packages

## 🧪 Testing

### Run All Tests
```bash
dotnet test HVOv9.sln
```

### Run Domain-Specific Tests
```bash
dotnet test HVO.Iot/HVO.Iot.sln
dotnet test HVO.RoofControllerV4/HVO.RoofControllerV4.sln
```

### Coverage Collection
```bash
dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

## 🐳 Docker

### Full Stack
```bash
docker compose up
```

### Domain-Specific Services
```bash
docker compose -f HVO.RoofControllerV4/docker-compose.yml up
docker compose -f HVO.SkyMonitorV5/docker-compose.yml up
docker compose -f HVO.WebSite/docker-compose.yml up
```

## 📋 Solutions

- **HVOv9.sln** - Root solution with all projects (excludes iOS/benchmarks)
- **HVOv9.DevContainer.sln** - Dev container optimized (excludes iOS/benchmarks)
- **Domain Solutions** - Each domain has its own solution for focused development

## 🔧 Development Tools

- **VS Code**: Use `HVOv9.code-workspace` (parent directory) for multi-root workspace
- **VS Code Tasks**: Defined in `.vscode/tasks.json` for common operations
- **Launch Configs**: Debug configurations in `.vscode/launch.json`

## 📖 Documentation

Each domain and project has its own README with specific details:
- Architecture and design patterns
- Configuration requirements
- Setup instructions
- Usage examples
- Deployment notes

See individual domain folders for more information.

## 🚀 CI/CD

GitHub Actions workflows:
- **dotnet.yml** - Main workflow (all projects, triggered by core library changes)
- **Domain workflows** - Per-domain workflows triggered by domain-specific changes
  - `astronomy.yml`, `iot.yml`, `nina.yml`, etc.

## 📦 NuGet Packages

Some projects are packaged for distribution:
- `HVO.ZWOOptical.ASISDK` - ZWO camera SDK (v0.0.3)
- `HVO.Astronomy.CFITSIO` - FITS I/O library (v1.0.3)
- `HVO.Astronomy.CFITSIO.NativeAssets` - Native binaries (v1.0.3)

Local packages output to: `.LocalPackages/`

## 📝 Standards

See `.github/copilot-instructions.md` for:
- Coding standards
- Architecture patterns
- Testing requirements
- Documentation guidelines
