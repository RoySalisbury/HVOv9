# HVOv9 Repository Reorganization Plan

**Status**: Planning Phase  
**Branch**: `feature/reorganize-project-structure`  
**Date Created**: October 25, 2025  
**Author**: GitHub Copilot

## Executive Summary

This document outlines a comprehensive plan to reorganize the HVOv9 repository from a flat `src/` structure to a modular, project-centric hierarchy. The new structure groups related projects into logical domains (Astronomy, IoT, RoofController, SkyMonitor, WebSite) with their own solutions, tests, and Docker configurations.

## Goals

1. **Logical Grouping**: Organize projects by functional domain
2. **Independent Solutions**: Each domain has its own solution file for focused development
3. **Consolidated Docker**: Project-specific docker-compose files with root orchestration
4. **Clear Dependencies**: Maintain shared libraries at the root while domain projects nest together
5. **Backward Compatibility**: Preserve existing CI/CD workflows and build configurations

## Current Structure Analysis

### Current Project Inventory (26 projects)

#### Shared/Core Libraries (remain at root)
- `HVO/` - Core library with Result<T>, IoT abstractions
- `HVO.DataModels/` - Entity Framework models and repositories
- `HVO.SourceGenerators/` - Build-time code generation
- `HVO.WebSite.Themes/` - Shared Blazor UI components and themes

#### IoT Devices Domain (move to HVO.Iot/)
- `HVO.Iot.Devices/` - Hardware device implementations (future NuGet package)
- `HVO.Iot.Devices.Tests/`

#### Astronomy Domain (move to HVO.Astronomy/)
- `HVO.Astronomy.CFITSIO/` - FITS file I/O library
- `HVO.Astronomy.CFITSIO.Tests/`
- `HVO.Astronomy.CFITSIO.NativeAssets/` - Platform-specific native binaries

#### NINA Integration (move to HVO.NINA/)
- `HVO.NinaClient/` - NINA API client library
- `HVO.NinaClient.Tests/` (if exists)

#### TheSkyX Integration (move to HVO.TheSkyX/)
- `HVO.TheSkyX/` - TheSkyX automation library
- `HVO.TheSkyX.Tests/` (if exists)

#### ZWO Camera SDK (move to HVO.ZWOOptical/)
- `HVO.ZWOOptical.ASISDK/` - ZWO ASI camera SDK wrapper
- `HVO.ZWOOptical.ASISDK.Tests/` (if exists)

#### Roof Controller V4 (move to HVO.RoofControllerV4/)
- `HVO.RoofControllerV4.Common/` - Shared DTOs and models
- `HVO.RoofControllerV4.RPi/` - Raspberry Pi controller app (Blazor Server)
- `HVO.RoofControllerV4.RPi.Tests/`

#### iOS/MAUI Apps (move to HVO.iOS/)
- `HVO.RoofControllerV4.iPad/` - Roof controller iOS/iPadOS app (MAUI)

#### Playground/Development Tools (move to HVO.Playground/)
- `HVO.Playground.CLI/` - General-purpose CLI playground
- `HVO.GpioTestApp/` - GPIO hardware testing

#### Sky Monitor V4 (move to HVO.SkyMonitorV4/)
- `HVO.SkyMonitorV4.RPi/` - Legacy V4 implementation
- `HVO.SkyMonitorV4.CLI/` - CLI tools

#### Sky Monitor V5 (move to HVO.SkyMonitorV5/)
- `HVO.SkyMonitorV5.Data/` - Data models and EF context
- `HVO.SkyMonitorV5.RPi/` - V5 implementation with WASM viewer
- `HVO.SkyMonitorV5.RPi.Tests/`
- `HVO.SkyMonitorV5.RPi.Benchmarks/` - Performance benchmarks
- `HVO.SkyMonitorV5.RPi.Stress/` - Stress testing

#### WebSite (move to HVO.WebSite/)
- `HVO.WebSite.v9/` - Main observatory website (Blazor Server)
- `HVO.WebSite.Playground/` - Development/testing site
- `HVO.WebSite.Playground.Tests/`

#### Utilities/Tools (legacy - to be removed)
- These projects are being moved to dedicated domains above

### Current Solution Structure
- `HVOv9.slnx` - XML-based solution (all projects)
- `HVOv9.slnf` - Solution filter
- `HVOv9.DevContainer.slnx` - Dev container optimized solution
- `HVOv9.DevContainer.slnf` - Dev container filter

### Current Docker Assets
- `HVO.RoofControllerV4.RPi/Dockerfile`
- `HVO.SkyMonitorV5.RPi/Dockerfile`
- No docker-compose files currently

## Proposed New Structure

```
/HVOv9
├── .devcontainer/                         # Containerized development environment config
├── .github/                               # CI/CD workflows using GitHub Actions
│   └── workflows/
│       └── dotnet.yml                     # Update paths for nested projects
├── .localPackages/                        # Local package output for easy reference
├── .vscode/                               # VS Code workspace settings
│   ├── tasks.json                         # Update project paths
│   └── launch.json                        # Update project paths
├── benchmarks/                            # Performance benchmark results (unchanged)
├── docs/                                  # Documentation (unchanged)
├── reports/                               # Test/benchmark reports (unchanged)
├── scripts/                               # Build/deployment scripts
│   ├── deploy-roofcontroller-rpi.sh      # Update paths
│   ├── deploy-skymonitor-rpi.sh          # Update paths
│   └── ...
├── .dockerignore                          # Global rules for all Docker builds
├── .gitignore                             # (unchanged)
├── Directory.Build.props                  # Consistent build settings for all projects
├── Directory.Packages.props               # Centralized NuGet package versioning (CPVM)
├── global.json                            # .NET SDK version pinning
├── HVOv9.code-workspace                   # VS Code multi-root workspace
├── NuGet.config                           # (unchanged)
├── README.md                              # Update structure references
│
├── src/
│   ├── HVO/                               # Core library (shared by all)
│   │   ├── HVO.csproj
│   │   ├── Result.cs
│   │   ├── ComponentModel/
│   │   └── Iot/Devices/                   # Abstractions only
│   │
│   ├── HVO.DataModels/                    # Shared data models (EF Core)
│   │   ├── HVO.DataModels.csproj
│   │   ├── Data/
│   │   └── Models/
│   │
│   ├── HVO.SourceGenerators/              # Build-time code generation
│   │   └── HVO.SourceGenerators.csproj
│   │
│   ├── HVO.WebSite.Themes/                # Shared Blazor themes
│   │   ├── HVO.WebSite.Themes.csproj
│   │   └── wwwroot/
│   │
│   ├── HVO.Iot/                           # IoT device implementations
│   │   ├── HVO.Iot.Devices/
│   │   │   ├── HVO.Iot.Devices.csproj
│   │   │   └── ...
│   │   ├── HVO.Iot.Devices.Tests/
│   │   │   └── HVO.Iot.Devices.Tests.csproj
│   │   └── HVO.Iot.sln                    # IoT-focused solution (future NuGet package)
│   │
│   ├── HVO.Astronomy/                     # Astronomy domain
│   │   ├── HVO.Astronomy.CFITSIO/
│   │   │   ├── HVO.Astronomy.CFITSIO.csproj
│   │   │   └── ...
│   │   ├── HVO.Astronomy.CFITSIO.NativeAssets/
│   │   │   └── HVO.Astronomy.CFITSIO.NativeAssets.csproj
│   │   ├── HVO.Astronomy.CFITSIO.Tests/
│   │   │   └── HVO.Astronomy.CFITSIO.Tests.csproj
│   │   └── HVO.Astronomy.sln              # Astronomy-focused solution
│   │
│   ├── HVO.NINA/                          # NINA integration
│   │   ├── HVO.NinaClient/
│   │   │   └── HVO.NinaClient.csproj
│   │   ├── HVO.NinaClient.Tests/
│   │   │   └── HVO.NinaClient.Tests.csproj
│   │   └── HVO.NINA.sln
│   │
│   ├── HVO.TheSkyX/                       # TheSkyX integration
│   │   ├── HVO.TheSkyX/
│   │   │   └── HVO.TheSkyX.csproj
│   │   ├── HVO.TheSkyX.Tests/
│   │   │   └── HVO.TheSkyX.Tests.csproj
│   │   └── HVO.TheSkyX.sln
│   │
│   ├── HVO.ZWOOptical/                    # ZWO camera integration
│   │   ├── HVO.ZWOOptical.ASISDK/
│   │   │   └── HVO.ZWOOptical.ASISDK.csproj
│   │   ├── HVO.ZWOOptical.ASISDK.Tests/
│   │   │   └── HVO.ZWOOptical.ASISDK.Tests.csproj
│   │   └── HVO.ZWOOptical.sln
│   │
│   ├── HVO.RoofControllerV4/              # Observatory roof control system
│   │   ├── HVO.RoofControllerV4.Common/
│   │   │   └── HVO.RoofControllerV4.Common.csproj
│   │   ├── HVO.RoofControllerV4.RPi/
│   │   │   ├── HVO.RoofControllerV4.RPi.csproj
│   │   │   ├── Dockerfile
│   │   │   └── .dockerignore
│   │   ├── HVO.RoofControllerV4.RPi.Tests/
│   │   │   └── HVO.RoofControllerV4.RPi.Tests.csproj
│   │   ├── HVO.RoofControllerV4.sln
│   │   └── docker-compose.yml             # Project-specific services
│   │
│   ├── HVO.iOS/                           # iOS/MAUI applications
│   │   ├── HVO.RoofControllerV4.iPad/
│   │   │   └── HVO.RoofControllerV4.iPad.csproj
│   │   └── HVO.iOS.sln                    # iOS/MAUI-focused solution
│   │
│   ├── HVO.SkyMonitorV4/                  # Legacy sky monitoring (V4)
│   │   ├── HVO.SkyMonitorV4.RPi/
│   │   │   ├── HVO.SkyMonitorV4.RPi.csproj
│   │   │   └── Dockerfile
│   │   ├── HVO.SkyMonitorV4.CLI/
│   │   │   └── HVO.SkyMonitorV4.CLI.csproj
│   │   ├── HVO.SkyMonitorV4.sln
│   │   └── docker-compose.yml
│   │
│   ├── HVO.SkyMonitorV5/                  # Current sky monitoring system
│   │   ├── HVO.SkyMonitorV5.Data/
│   │   │   └── HVO.SkyMonitorV5.Data.csproj
│   │   ├── HVO.SkyMonitorV5.RPi/
│   │   │   ├── HVO.SkyMonitorV5.RPi.csproj
│   │   │   ├── Dockerfile
│   │   │   ├── .dockerignore
│   │   │   └── Data/
│   │   │       ├── catalogs/              # Star catalogs
│   │   │       └── configuration/         # Config files
│   │   ├── HVO.SkyMonitorV5.RPi.Tests/
│   │   │   └── HVO.SkyMonitorV5.RPi.Tests.csproj
│   │   ├── HVO.SkyMonitorV5.RPi.Benchmarks/
│   │   │   └── HVO.SkyMonitorV5.RPi.Benchmarks.csproj
│   │   ├── HVO.SkyMonitorV5.RPi.Stress/
│   │   │   └── HVO.SkyMonitorV5.RPi.Stress.csproj
│   │   ├── HVO.SkyMonitorV5.sln
│   │   └── docker-compose.yml             # MinIO, monitoring services
│   │
│   ├── HVO.WebSite/                       # Observatory web applications
│   │   ├── HVO.WebSite.v9/
│   │   │   ├── HVO.WebSite.v9.csproj
│   │   │   ├── Dockerfile
│   │   │   └── .dockerignore
│   │   ├── HVO.WebSite.Playground/
│   │   │   ├── HVO.WebSite.Playground.csproj
│   │   │   ├── Dockerfile
│   │   │   └── .dockerignore
│   │   ├── HVO.WebSite.Playground.Tests/
│   │   │   └── HVO.WebSite.Playground.Tests.csproj
│   │   ├── HVO.WebSite.sln
│   │   └── docker-compose.yml             # Web services, DB, etc.
│   │
│   ├── HVO.Playground/                    # Development/testing utilities
│   │   ├── HVO.Playground.CLI/
│   │   │   └── HVO.Playground.CLI.csproj
│   │   ├── HVO.GpioTestApp/
│   │   │   └── HVO.GpioTestApp.csproj
│   │   └── HVO.Playground.sln
│   │
│   ├── HVOv9.sln                          # Root solution (all projects)
│   ├── HVOv9.DevContainer.sln             # Dev container optimized
│   ├── docker-compose.yml                 # Root orchestration (includes all project composes)
│   └── docker-compose.override.yml        # Local development overrides
│
└── tests/                                 # Integration/E2E tests (future)
    └── HVO.Integration.Tests/
```

## Project Grouping Strategy

### Domain-Based Organization

| Domain | Projects | Dependencies |
|--------|----------|--------------|
| **Core** | HVO, HVO.DataModels, HVO.SourceGenerators, HVO.WebSite.Themes | None (shared by all) |
| **IoT Devices** | HVO.Iot.Devices | → HVO (future NuGet package) |
| **Astronomy** | HVO.Astronomy.CFITSIO, HVO.Astronomy.CFITSIO.NativeAssets | → HVO |
| **NINA** | HVO.NinaClient | → HVO |
| **TheSkyX** | HVO.TheSkyX | → HVO |
| **ZWO Optical** | HVO.ZWOOptical.ASISDK | → HVO |
| **RoofController V4** | Common, RPi | → HVO, HVO.Iot.Devices, HVO.WebSite.Themes |
| **iOS/MAUI Apps** | RoofControllerV4.iPad | → HVO, HVO.Iot.Devices, HVO.RoofControllerV4.Common |
| **SkyMonitor V4** | RPi, CLI | → HVO, HVO.Astronomy.CFITSIO |
| **SkyMonitor V5** | Data, RPi, Tests, Benchmarks, Stress | → HVO, HVO.SkyMonitorV5.Data, HVO.Astronomy.CFITSIO, HVO.ZWOOptical.ASISDK, HVO.WebSite.Themes |
| **WebSite** | v9, Playground | → HVO, HVO.DataModels, HVO.WebSite.Themes |
| **Playground** | Playground.CLI, GpioTestApp | → HVO, HVO.Iot.Devices |

### Solution Files Strategy

Each domain gets its own focused solution for development:
- `HVO.Iot.sln` - IoT device implementations (future NuGet package)
- `HVO.Astronomy.sln` - Astronomy libraries only
- `HVO.NINA.sln` - NINA client only
- `HVO.TheSkyX.sln` - TheSkyX integration only
- `HVO.ZWOOptical.sln` - ZWO camera SDK only
- `HVO.RoofControllerV4.sln` - Roof controller projects
- `HVO.iOS.sln` - iOS/MAUI applications
- `HVO.SkyMonitorV4.sln` - V4 monitoring
- `HVO.SkyMonitorV5.sln` - V5 monitoring
- `HVO.WebSite.sln` - Web applications
- `HVO.Playground.sln` - Development tools
- `HVOv9.sln` (root) - Everything (for full builds)
- `HVOv9.DevContainer.sln` (root) - Dev container optimized subset

## Migration Steps

### Phase 1: Preparation (No Code Changes)
**Duration**: 1-2 hours  
**Risk**: Low

1. **Document Current State**
   - [x] Create this plan document
   - [ ] Export current project dependency graph
   - [ ] Document all solution filters and their purposes
   - [ ] List all scripts that reference project paths

2. **Validate Tests**
   - [ ] Run full test suite on current structure: `dotnet test src/HVOv9.sln`
   - [ ] Capture baseline coverage report
   - [ ] Document any pre-existing test failures

3. **Create Tracking Branch**
   - [x] Create `feature/reorganize-project-structure` branch
   - [ ] Push branch to remote for tracking

### Phase 2: Core Library Restructuring
**Duration**: 2-3 hours  
**Risk**: Medium (affects all projects)

4. **Move Shared Libraries** (remain at src/ root)
   - [ ] Verify these stay at `src/HVO/`, `src/HVO.DataModels/`, etc.
   - [ ] Update any self-referencing paths in project files

### Phase 3: Domain Project Migration
**Duration**: 4-6 hours  
**Risk**: Medium-High

5. **Create Domain Directories**
   ```bash
   mkdir -p src/HVO.Iot
   mkdir -p src/HVO.Astronomy
   mkdir -p src/HVO.NINA
   mkdir -p src/HVO.TheSkyX
   mkdir -p src/HVO.ZWOOptical
   mkdir -p src/HVO.RoofControllerV4
   mkdir -p src/HVO.iOS
   mkdir -p src/HVO.SkyMonitorV4
   mkdir -p src/HVO.SkyMonitorV5
   mkdir -p src/HVO.WebSite
   mkdir -p src/HVO.Playground
   ```

6. **Move IoT Device Projects**
   ```bash
   git mv src/HVO.Iot.Devices src/HVO.Iot/
   git mv src/HVO.Iot.Devices.Tests src/HVO.Iot/
   ```
   - [ ] Update all ProjectReference paths in moved projects
   - [ ] Create `src/HVO.Iot/HVO.Iot.sln`
   - [ ] Add note about future NuGet packaging strategy
   - [ ] Test build: `dotnet build src/HVO.Iot/HVO.Iot.sln`

7. **Move Astronomy Projects**
   ```bash
   git mv src/HVO.Astronomy.CFITSIO src/HVO.Astronomy/
   git mv src/HVO.Astronomy.CFITSIO.NativeAssets src/HVO.Astronomy/
   git mv src/HVO.Astronomy.CFITSIO.Tests src/HVO.Astronomy/
   ```
   - [ ] Update all ProjectReference paths in moved projects
   - [ ] Create `src/HVO.Astronomy/HVO.Astronomy.sln`
   - [ ] Test build: `dotnet build src/HVO.Astronomy/HVO.Astronomy.sln`

8. **Move NINA Projects**
   ```bash
   git mv src/HVO.NinaClient src/HVO.NINA/
   # Create tests if missing
   ```
   - [ ] Update ProjectReference paths
   - [ ] Create `src/HVO.NINA/HVO.NINA.sln`
   - [ ] Test build

9. **Move TheSkyX Projects**
   ```bash
   git mv src/HVO.TheSkyX src/HVO.TheSkyX/HVO.TheSkyX/
   # Create tests if missing
   ```
   - [ ] Update ProjectReference paths
   - [ ] Create `src/HVO.TheSkyX/HVO.TheSkyX.sln`
   - [ ] Test build

10. **Move ZWO Optical Projects**
   ```bash
   git mv src/HVO.ZWOOptical.ASISDK src/HVO.ZWOOptical/
   # Create tests if missing
   ```
   - [ ] Update ProjectReference paths
   - [ ] Create `src/HVO.ZWOOptical/HVO.ZWOOptical.sln`
   - [ ] Test build

11. **Move Roof Controller V4 Projects** (RPi only - iPad moves separately)
    ```bash
    git mv src/HVO.RoofControllerV4.Common src/HVO.RoofControllerV4/
    git mv src/HVO.RoofControllerV4.RPi src/HVO.RoofControllerV4/
    git mv src/HVO.RoofControllerV4.RPi.Tests src/HVO.RoofControllerV4/
    ```
    - [ ] Update all ProjectReference paths (use `../` to reach core libs)
    - [ ] Update references to HVO.Iot.Devices (now at `../../HVO.Iot/HVO.Iot.Devices/`)
    - [ ] Create `src/HVO.RoofControllerV4/HVO.RoofControllerV4.sln`
    - [ ] Create `src/HVO.RoofControllerV4/docker-compose.yml`
    - [ ] Test build
    - [ ] Test Docker build: `docker build src/HVO.RoofControllerV4/HVO.RoofControllerV4.RPi`

12. **Move iOS/MAUI Projects**
    ```bash
    git mv src/HVO.RoofControllerV4.iPad src/HVO.iOS/
    ```
    - [ ] Update ProjectReference paths to reach HVO, HVO.Iot, and HVO.RoofControllerV4.Common
    - [ ] Create `src/HVO.iOS/HVO.iOS.sln`
    - [ ] Test build (macOS required for MAUI iOS)
    - [ ] Update iOS runner scripts in `scripts/`

13. **Move Sky Monitor V4 Projects**
    ```bash
    git mv src/HVO.SkyMonitorV4.RPi src/HVO.SkyMonitorV4/
    git mv src/HVO.SkyMonitorV4.CLI src/HVO.SkyMonitorV4/
    ```
    - [ ] Update ProjectReference paths
    - [ ] Create `src/HVO.SkyMonitorV4/HVO.SkyMonitorV4.sln`
    - [ ] Create `src/HVO.SkyMonitorV4/docker-compose.yml`
    - [ ] Test build

14. **Move Sky Monitor V5 Projects**
    ```bash
    git mv src/HVO.SkyMonitorV5.Data src/HVO.SkyMonitorV5/
    git mv src/HVO.SkyMonitorV5.RPi src/HVO.SkyMonitorV5/
    git mv src/HVO.SkyMonitorV5.RPi.Tests src/HVO.SkyMonitorV5/
    git mv src/HVO.SkyMonitorV5.RPi.Benchmarks src/HVO.SkyMonitorV5/
    git mv src/HVO.SkyMonitorV5.RPi.Stress src/HVO.SkyMonitorV5/
    ```
    - [ ] Update ProjectReference paths
    - [ ] Create `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.sln`
    - [ ] Create `src/HVO.SkyMonitorV5/docker-compose.yml` (MinIO, monitoring)
    - [ ] Test build
    - [ ] Test Docker build
    - [ ] Run benchmarks to verify no regression

15. **Move WebSite Projects**
    ```bash
    git mv src/HVO.WebSite.v9 src/HVO.WebSite/
    git mv src/HVO.WebSite.Playground src/HVO.WebSite/
    git mv src/HVO.WebSite.Playground.Tests src/HVO.WebSite/
    ```
    - [ ] Update ProjectReference paths
    - [ ] Create `src/HVO.WebSite/HVO.WebSite.sln`
    - [ ] Create `src/HVO.WebSite/docker-compose.yml`
    - [ ] Test build

16. **Move Playground/Utilities Projects**
    ```bash
    git mv src/HVO.Playground.CLI src/HVO.Playground/
    git mv src/HVO.GpioTestApp src/HVO.Playground/
    ```
    - [ ] Update ProjectReference paths
    - [ ] Create `src/HVO.Playground/HVO.Playground.sln`
    - [ ] Test build

### Phase 4: Solution and Configuration Files
**Duration**: 2-3 hours  
**Risk**: Medium

17. **Create/Update Root Solutions**
    - [ ] Update `src/HVOv9.sln` to reference all new project paths
    - [ ] Update `src/HVOv9.DevContainer.sln` with subset
    - [ ] Remove old `.slnf` and `.slnx` files if obsolete
    - [ ] Test: `dotnet build src/HVOv9.sln`
    - [ ] Test: `dotnet build src/HVOv9.DevContainer.sln`

18. **Create Docker Orchestration**
    - [ ] Create `src/docker-compose.yml` that includes all project compose files:
      ```yaml
      version: '3.8'
      include:
        - path: ./HVO.RoofControllerV4/docker-compose.yml
        - path: ./HVO.SkyMonitorV5/docker-compose.yml
        - path: ./HVO.WebSite/docker-compose.yml
      ```
    - [ ] Create `src/docker-compose.override.yml` for local dev
    - [ ] Test full stack: `docker-compose -f src/docker-compose.yml up`

19. **Update Build Configuration**
    - [ ] Verify `Directory.Build.props` still applies to all projects
    - [ ] Verify `Directory.Packages.props` (CPVM) still works
    - [ ] Verify `global.json` applies correctly
    - [ ] Update `NuGet.config` if necessary

### Phase 5: Tooling and Scripts Update
**Duration**: 2-3 hours  
**Risk**: Medium

20. **Update VS Code Configuration**
    - [ ] Update `.vscode/tasks.json` paths:
      - `build:roofv4:debug` → `src/HVO.RoofControllerV4/HVO.RoofControllerV4.RPi/`
      - `build:playground:debug` → `src/HVO.WebSite/HVO.WebSite.Playground/`
      - `build:v9:debug` → `src/HVO.WebSite/HVO.WebSite.v9/`
    - [ ] Update `.vscode/launch.json` paths
    - [ ] Update `HVOv9.code-workspace` folder paths

21. **Update Deployment Scripts**
    - [ ] `scripts/deploy-roofcontroller-rpi.sh` → update project path
    - [ ] `scripts/deploy-skymonitor-rpi.sh` → update project path
    - [ ] `scripts/run-maui-ios.sh` → update project path to `src/HVO.iOS/HVO.RoofControllerV4.iPad/`
    - [ ] `scripts/copy-catalog.sh` → update data paths
    - [ ] Test all scripts

22. **Update GitHub Actions Workflows**
    - [ ] `.github/workflows/dotnet.yml`:
      - Update solution path to `src/HVOv9.sln`
      - Update test project paths
      - Update artifact paths
    - [ ] Test workflow on branch

23. **Update Documentation**
    - [ ] Update `README.md` with new structure
    - [ ] Update `docs/TODO.md` with new paths
    - [ ] Update all `docs/projects/*.md` with new paths
    - [ ] Update `docs/guides/blazor-component-best-practices.md` examples

### Phase 6: Validation and Testing
**Duration**: 2-4 hours  
**Risk**: Low

24. **Full Build Validation**
    - [ ] Clean build root solution: `dotnet clean src/HVOv9.sln && dotnet build src/HVOv9.sln`
    - [ ] Build each domain solution individually
    - [ ] Verify no broken ProjectReferences
    - [ ] Verify NuGet package restore works

25. **Test Suite Validation**
    - [ ] Run full test suite: `dotnet test src/HVOv9.sln`
    - [ ] Compare results with baseline (Phase 1)
    - [ ] Fix any path-related test failures
    - [ ] Verify coverage collection still works

26. **Docker Validation**
    - [ ] Build all Docker images
    - [ ] Test RoofController V4 container
    - [ ] Test SkyMonitor V5 container
    - [ ] Test WebSite containers
    - [ ] Test full docker-compose stack

27. **Dev Container Validation**
    - [ ] Rebuild dev container with new paths
    - [ ] Verify `post-create.sh` works with new structure
    - [ ] Verify solution opens correctly
    - [ ] Verify tasks work
    - [ ] Verify launch configurations work

28. **CI/CD Validation**
    - [ ] Push branch to trigger GitHub Actions
    - [ ] Verify workflow completes successfully
    - [ ] Verify artifacts are created correctly

### Phase 7: Cleanup and Finalization
**Duration**: 1 hour  
**Risk**: Low

29. **Final Cleanup**
    - [ ] Remove any obsolete files/directories
    - [ ] Remove old solution filters if unused
    - [ ] Update `.gitignore` if needed
    - [ ] Run `git status` to ensure no untracked changes

30. **Documentation Review**
    - [ ] Update this plan with actual findings
    - [ ] Create migration summary document
   - [ ] Document any deviations from plan
   - [ ] Update workspace standards in copilot-instructions.md

31. **Code Review Preparation**
    - [ ] Create PR with detailed description
    - [ ] Include before/after structure comparison
    - [ ] Document all breaking changes
    - [ ] Tag any follow-up work as issues

32. **Merge to Main**
    - [ ] Get PR approval
    - [ ] Squash commits or keep history (decide)
    - [ ] Merge to main
    - [ ] Tag release if appropriate
    - [ ] Update local workspaces

## Expected Benefits

### Developer Experience
- **Faster Build Times**: Domain solutions build only relevant projects
- **Clearer Context**: Project grouping makes intent obvious
- **Easier Onboarding**: New developers can focus on specific domains

### CI/CD
- **Targeted Builds**: Can build/test individual domains
- **Faster Feedback**: Domain-specific CI pipelines possible
- **Better Caching**: Docker layer caching per domain

### Deployment
- **Independent Services**: Each domain can deploy independently
- **Clearer Dependencies**: Docker compose shows service relationships
- **Easier Rollbacks**: Domain-level rollback granularity

### Maintenance
- **Logical Organization**: Related code stays together
- **Reduced Coupling**: Clear boundaries between domains
- **Better Discoverability**: IDE solution explorer is more navigable

## Potential Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Broken ProjectReferences** | High | Systematic path updates, test each domain |
| **CI/CD pipeline failures** | High | Test workflows on branch before merge |
| **Docker build context issues** | Medium | Careful Dockerfile path adjustments, test builds |
| **Developer workflow disruption** | Medium | Clear documentation, gradual rollout |
| **Git history complexity** | Low | Use `git mv` to preserve history, clear commits |
| **Path length issues (Windows)** | Low | Monitor path lengths, use shorter names if needed |

## Success Criteria

- [ ] All domain solutions build independently
- [ ] Root solution builds all projects
- [ ] All tests pass with same results as baseline
- [ ] All Docker images build successfully
- [ ] Full docker-compose stack runs
- [ ] Dev container works with new structure
- [ ] CI/CD pipeline passes on branch
- [ ] All deployment scripts work
- [ ] Documentation reflects new structure
- [ ] No regression in build times
- [ ] No regression in test coverage

## Timeline Estimate

| Phase | Duration | Cumulative |
|-------|----------|------------|
| Phase 1: Preparation | 1-2 hours | 2 hours |
| Phase 2: Core Libraries | 2-3 hours | 5 hours |
| Phase 3: Domain Migration | 4-6 hours | 11 hours |
| Phase 4: Solutions/Config | 2-3 hours | 14 hours |
| Phase 5: Tooling Update | 2-3 hours | 17 hours |
| Phase 6: Validation | 2-4 hours | 21 hours |
| Phase 7: Cleanup | 1 hour | 22 hours |

**Total Estimated Time**: 20-22 hours (2.5-3 work days)

## Implementation Notes

### ProjectReference Path Updates

When moving projects, update ProjectReference elements:

**Before** (flat structure):
```xml
<ProjectReference Include="../HVO/HVO.csproj" />
<ProjectReference Include="../HVO.DataModels/HVO.DataModels.csproj" />
```

**After** (nested structure from domain folder):
```xml
<ProjectReference Include="../../HVO/HVO.csproj" />
<ProjectReference Include="../../HVO.DataModels/HVO.DataModels.csproj" />
```

### Docker Compose Include Pattern

Root `src/docker-compose.yml`:
```yaml
version: '3.8'

include:
  - path: ./HVO.RoofControllerV4/docker-compose.yml
  - path: ./HVO.SkyMonitorV5/docker-compose.yml
  - path: ./HVO.WebSite/docker-compose.yml

networks:
  hvo-network:
    driver: bridge
```

Domain-specific `src/HVO.SkyMonitorV5/docker-compose.yml`:
```yaml
version: '3.8'

services:
  skymonitor:
    build:
      context: ../..
      dockerfile: src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/Dockerfile
    ports:
      - "5000:8080"
    networks:
      - hvo-network

  minio:
    image: minio/minio
    # ... MinIO config

networks:
  hvo-network:
    external: true
```

### Solution File Creation

Use `dotnet sln` commands:
```bash
# Create domain solution
dotnet new sln -n HVO.SkyMonitorV5 -o src/HVO.SkyMonitorV5

# Add projects
dotnet sln src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.sln add \
  src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.Data/HVO.SkyMonitorV5.Data.csproj \
  src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/HVO.SkyMonitorV5.RPi.csproj \
  src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Tests/HVO.SkyMonitorV5.RPi.Tests.csproj

# Add shared dependencies
dotnet sln src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.sln add \
  src/HVO/HVO.csproj \
  src/HVO.WebSite.Themes/HVO.WebSite.Themes.csproj \
  src/HVO.Astronomy/HVO.Astronomy.CFITSIO/HVO.Astronomy.CFITSIO.csproj
```

## Rollback Plan

If issues arise during migration:

1. **Before Merge**: Simply abandon the `feature/reorganize-project-structure` branch
2. **After Merge**: Revert merge commit and document issues
3. **Partial Rollback**: Cherry-pick successful migrations, revert problematic ones

## Follow-Up Work

After successful reorganization:

- [ ] Create domain-specific CI/CD workflows
- [ ] Evaluate NuGet packaging strategy (pack domains independently?)
- [ ] Consider monorepo tooling (Nuke, Cake, or native workspaces)
- [ ] Update workspace coding standards
- [ ] Create domain-specific documentation
- [ ] Evaluate integration test structure

## Questions for Review

1. Should we keep both `.sln` and `.slnx` formats, or standardize on one?
2. Do we need `HVOv9.DevContainer.sln` or can we use solution filters?
3. Should WebSite.Themes stay at root or move into HVO.WebSite/?
4. Should we keep V4 projects or archive them first?
5. Do we want to introduce a `tests/` directory for integration tests?
6. Should IoT.Devices be packaged as a NuGet package before or after reorganization?
7. Should iOS projects eventually include multiple apps or stay single-purpose?

## Approval and Sign-Off

- [ ] Developer Review: _____________________
- [ ] Architecture Review: _____________________
- [ ] DevOps Review: _____________________
- [ ] Final Approval: _____________________

---

**Document Version**: 1.0  
**Last Updated**: October 25, 2025  
**Status**: Ready for Review
