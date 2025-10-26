# HVOv9 Documentation Index

Welcome to the HVOv9 documentation! This index provides quick navigation to all project documentation, guides, and resources.

## 📚 Quick Start

- **[Main README](../README.md)** - Project overview, tech stack, and getting started
- **[Coding Standards](.github/copilot-instructions.md)** - Workspace-wide development guidelines
- **[Dev Container Setup](guides/devcontainer-environment-setup.md)** - Setting up your development environment

## 🎯 Domain Documentation

HVOv9 is organized into 11 functional domains, each with comprehensive documentation:

### Core Libraries
- **[HVO](../src/HVO/README.md)** - Core library with Result<T> pattern, utilities, base types
- **[HVO.DataModels](../src/HVO.DataModels/README.md)** - EF Core DbContext, entities, repositories
- **[HVO.SourceGenerators](../src/HVO.SourceGenerators/README.md)** - Roslyn source generators
- **[HVO.WebSite.Themes](../src/HVO.WebSite.Themes/README.md)** - Razor Class Library with HVO Dark theme

### Production Domains

#### 🔭 Astronomy
**[HVO.Astronomy](../src/HVO.Astronomy/README.md)** - FITS file I/O and astronomical data processing
- Projects: CFITSIO wrapper, FITS tests
- Key Features: FITS reading/writing, header management, image data processing

#### 🔌 IoT
**[HVO.Iot](../src/HVO.Iot/README.md)** - Hardware device abstractions and GPIO control
- Projects: IoT.Devices, IoT.Devices.Tests
- Key Features: Limit switches, relays, I²C HATs, watchdog timers
- **Project Details**: [IoT Devices Documentation](projects/iot-devices/)

#### 🌠 NINA Integration
**[HVO.NINA](../src/HVO.NINA/README.md)** - N.I.N.A. API client for imaging coordination
- Projects: NinaClient
- Key Features: Camera control, telescope operations, real-time status
- **Project Details**: [NINA Client Documentation](projects/nina-client/)

#### 🏠 Roof Controller V4
**[HVO.RoofControllerV4](../src/HVO.RoofControllerV4/README.md)** - Observatory roof automation
- Projects: Common, RPi, RPi.Tests
- Key Features: Open/close automation, safety systems, REST API
- **Project Details**: [Roof Controller V4 Documentation](projects/roof-controller-v4-rpi/)
- **Deployment**: [Docker Guide](roofcontrollerv4-docker.md)

#### 🌌 Sky Monitor V5
**[HVO.SkyMonitorV5](../src/HVO.SkyMonitorV5/README.md)** - High-performance all-sky camera
- Projects: RPi, RPi.Tests, RPi.Benchmarks, RPi.Stress
- Key Features: Real-time star detection, cloud coverage, ~3.5s processing
- **Project Details**: [Sky Monitor V5 Documentation](projects/sky-monitor-v5/)
- **Deployment**: [Docker Guide](skymonitor-v5-docker.md)
- **Operations**: [Operations Runbook](skymonitor-v5-operations-runbook.md)
- **Migration**: [JSON Migration Guide](skymonitor-v5-json-migration-guide.md)
- **Performance**: [Performance Benchmarks](performance-benchmarks.md)

#### 🌐 Web Site (Active Development)
**[HVO.WebSite](../src/HVO.WebSite/README.md)** - Observatory dashboard and monitoring
- Projects: WebSite.v9, WebSite.Playground, WebSite.Playground.Tests
- Key Features: Real-time dashboard, weather/roof/sky integration, REST API
- **Project Details**: [Website Playground Documentation](projects/website-playground/)

#### 📱 iOS
**[HVO.iOS](../src/HVO.iOS/README.md)** - .NET MAUI iPad application
- Projects: RoofControllerV4.iPad
- Key Features: Touch-optimized UI, MVVM pattern, network autodiscovery

### Specialized Domains

#### 📷 ZWO Optical
**[HVO.ZWOOptical](../src/HVO.ZWOOptical/README.md)** - ZWO ASI camera SDK integration
- Projects: ASISDK
- Key Features: Camera discovery, 16-bit RAW capture, cooling control

#### 🔭 TheSkyX (Planned)
**[HVO.TheSkyX](../src/HVO.TheSkyX/README.md)** - Software Bisque integration (WIP)
- Status: Planned for 2026
- Roadmap: Telescope control, camera integration, automation

### Legacy & Experimental

#### 🌌 Sky Monitor V4 (Deprecated)
**[HVO.SkyMonitorV4](../src/HVO.SkyMonitorV4/README.md)** - Legacy all-sky camera
- Status: EOL December 2026, migrate to V5
- Migration: See domain README for migration guide

#### 🧪 Playground
**[HVO.Playground](../src/HVO.Playground/README.md)** - Development utilities and experiments
- Projects: Playground.CLI, GpioTestApp
- Purpose: Rapid prototyping, hardware testing, batch processing

## 📖 Development Guides

### Getting Started
- **[Dev Container Environment Setup](guides/devcontainer-environment-setup.md)** - Complete environment configuration
- **[Dev Container Extensions](guides/dev-container-extensions.md)** - VS Code extensions and tooling
- **[GitHub Copilot Tools Setup](guides/github-copilot-tools-setup.md)** - AI-assisted development configuration

### Best Practices
- **[Blazor Component Best Practices](guides/blazor-component-best-practices.md)** - Component structure, scoped CSS/JS, MVVM patterns
- **[MSTest Standardization](testing/mstest-standardization.md)** - Testing patterns and conventions
- **[Coverage Badge Setup](guides/coverage-badge-setup.md)** - CI/CD test coverage reporting

## 🚀 Project Planning

### Active Projects
- **[.NET 10 Readiness Plan](projects/dotnet10-readiness-plan.md)** - Preparing for .NET 10 upgrade
- **[SkyMonitor V5 FITS Export Plan](projects/skymonitor-v5-fits-export-plan.md)** - FITS file export implementation

### Completed Projects
- **[Repository Reorganization Plan](projects/repository-reorganization-plan.md)** - Domain-based restructuring (✅ Complete)
- **[Coverage Benchmark Fix Summary](projects/coverage-benchmark-fix-summary.md)** - Performance optimization results

## 🔧 Operations & Deployment

### Docker Deployment
- **[RoofController V4 Docker Guide](roofcontrollerv4-docker.md)** - Containerized deployment on Raspberry Pi
- **[SkyMonitor V5 Docker Guide](skymonitor-v5-docker.md)** - Building and running SkyMonitor in containers
- **[SkyMonitor V5 Operations Runbook](skymonitor-v5-operations-runbook.md)** - Backup, restore, and change control

### Configuration Management
- **[SkyMonitor V5 JSON Migration Guide](skymonitor-v5-json-migration-guide.md)** - Migrating legacy configuration to SQLite

## 🎯 Domain-Specific Documentation

### IoT Devices
Navigate to **[projects/iot-devices/](projects/iot-devices/)** for:
- Hardware device specifications
- GPIO pin mappings
- I²C HAT configuration
- Troubleshooting guides

### NINA Client
Navigate to **[projects/nina-client/](projects/nina-client/)** for:
- API endpoint documentation
- WebSocket event handling
- Equipment coordination examples

### Roof Controller V4
Navigate to **[projects/roof-controller-v4-rpi/](projects/roof-controller-v4-rpi/)** for:
- Safety system design
- GPIO configuration
- API endpoint reference
- Deployment procedures

### Sky Monitor V5
Navigate to **[projects/sky-monitor-v5/](projects/sky-monitor-v5/)** for:
- Star detection algorithms
- Cloud coverage analysis
- Performance optimization
- FITS export configuration

### Website Playground
Navigate to **[projects/website-playground/](projects/website-playground/)** for:
- API development patterns
- Blazor component examples
- Service integration guides

## 📊 Performance & Benchmarks

- **[Performance Benchmarks](performance-benchmarks.md)** - System-wide performance metrics
  - SkyMonitor V5 processing times (RPi5 vs M2 Mac)
  - Star detection performance
  - Image processing benchmarks

## 🗺️ Project Roadmap

### Current Focus (2025 Q4)
- ✅ Repository reorganization complete
- ✅ SkyMonitor V5 production deployment
- ✅ Comprehensive documentation overhaul
- 🔄 WebSite.v9 dashboard development
- 📋 FITS export enhancement

### Upcoming (2026 Q1-Q2)
- TheSkyX integration foundation
- .NET 10 migration
- Advanced visualization features
- Observatory automation workflows

### Future Enhancements
- Multi-extension archival FITS
- Advanced WCS/plate solving
- Mobile app for Android
- Machine learning for sky conditions

## 🔍 Finding Documentation

### By Topic
- **Hardware Integration** → [IoT Domain](../src/HVO.Iot/README.md), [IoT Devices Project](projects/iot-devices/)
- **Astronomy/Imaging** → [Astronomy Domain](../src/HVO.Astronomy/README.md), [ZWO Optical Domain](../src/HVO.ZWOOptical/README.md)
- **Web Development** → [WebSite Domain](../src/HVO.WebSite/README.md), [Blazor Guide](guides/blazor-component-best-practices.md)
- **API Integration** → [NINA Domain](../src/HVO.NINA/README.md), [NINA Client Project](projects/nina-client/)
- **Testing** → [MSTest Standardization](testing/mstest-standardization.md), individual test project READMEs
- **Deployment** → [Docker Guides](#operations--deployment), domain deployment sections

### By Project Type
- **Raspberry Pi Applications** → RoofController V4, SkyMonitor V5 domains
- **Web Applications** → WebSite domain
- **Mobile Applications** → iOS domain
- **CLI Tools** → Playground domain
- **Libraries** → Core libraries, domain-specific libraries

### By Development Phase
- **Getting Started** → [Main README](../README.md), [Dev Container Setup](guides/devcontainer-environment-setup.md)
- **Active Development** → Domain READMEs, [Coding Standards](../.github/copilot-instructions.md)
- **Testing** → [MSTest Guide](testing/mstest-standardization.md), test project READMEs
- **Deployment** → [Docker Guides](#operations--deployment), operations runbooks

## 📝 Contributing to Documentation

When adding new documentation:

1. **Domain READMEs** - Place in `src/<DomainName>/README.md`
2. **Project-Specific Docs** - Place in `docs/projects/<project-name>/`
3. **General Guides** - Place in `docs/guides/`
4. **Testing Documentation** - Place in `docs/testing/`
5. **Update This Index** - Add links to new documentation here

### Documentation Standards
- Use clear, descriptive headings
- Include code examples where applicable
- Provide configuration samples
- Link to related documentation
- Keep examples up-to-date with current codebase

## 🆘 Support & Resources

- **Issues** - [GitHub Issues](https://github.com/RoySalisbury/HVOv9/issues)
- **Discussions** - [GitHub Discussions](https://github.com/RoySalisbury/HVOv9/discussions)
- **Main README** - [../README.md](../README.md)
- **Coding Standards** - [../.github/copilot-instructions.md](../.github/copilot-instructions.md)

---

**Last Updated**: October 25, 2025  
**Documentation Version**: 9.0 (aligned with HVOv9 project structure)
