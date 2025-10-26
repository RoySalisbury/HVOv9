# Coverage and Benchmark Fix Summary

**Date**: October 25, 2025  
**Branch**: `feature/reorganize-project-structure`  
**Phase**: Phase 6 (Validation and Testing)

## Overview

This document summarizes the completion of code coverage wiring and SkyMonitor V5 benchmark fix as part of the repository reorganization Phase 6 validation.

## Changes Implemented

### 1. Code Coverage Collection

#### Centralized Configuration

Created standardized coverage configuration at `src/coverage.runsettings`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Format>cobertura</Format>
          <Include>[HVO]*</Include>
          <Exclude>[*.Tests]*</Exclude>
          <SkipAutoProps>true</SkipAutoProps>
          <UseSourceLink>true</UseSourceLink>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

#### Test Project Updates

Added `coverlet.collector` package reference to all test projects:

- `src/HVO.Iot/HVO.Iot.Devices.Tests/HVO.Iot.Devices.Tests.csproj`
- `src/HVO.WebSite/HVO.WebSite.Playground.Tests/HVO.WebSite.Playground.Tests.csproj`
- `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Tests/HVO.SkyMonitorV5.RPi.Tests.csproj`

Existing projects already had the collector:
- `src/HVO.Astronomy/HVO.Astronomy.CFITSIO.Tests/HVO.Astronomy.CFITSIO.Tests.csproj`
- `src/HVO.RoofControllerV4/HVO.RoofControllerV4.RPi.Tests/HVO.RoofControllerV4.RPi.Tests.csproj`

#### CI/CD Integration

Updated `.github/workflows/dotnet.yml` to collect coverage in both unit and integration test jobs:

- Added `--settings coverage.runsettings` to test commands
- Updated artifact upload steps to include `**/TestResults/**/coverage.cobertura.xml`
- Coverage artifacts are now uploaded alongside test results for all runs

#### Documentation Updates

- Added coverage badge section to `README.md` with placeholder for future Shields.io endpoint
- Documented coverage collection in the README with note about Cobertura format

### 2. SkyMonitor V5 Benchmark Fix

#### Problem

The `ProcessedFrameEncoder` constructor signature was updated to require additional dependencies:
- `IFitsFrameEncoder`
- `IRigAcquisitionAdapter`
- `IOptionsMonitor<FitsExportOptions>`

This broke two benchmark files:
- `EndToEndPipelineBenchmarks.cs`
- `FrameFilterPipelineBenchmarks.cs`

#### Solution

Added benchmark-specific helper classes to both files:

1. **StaticOptionsMonitor<T>**: Minimal implementation of `IOptionsMonitor<T>` for benchmark scenarios
2. **NoopFitsFrameEncoder**: No-op implementation of `IFitsFrameEncoder` (not used when FITS export is disabled)
3. **DummyRigAdapter**: Lightweight `IRigAcquisitionAdapter` providing the required `RigSpec` reference

Added missing using directives:
- `using HVO.SkyMonitorV5.RPi.Cameras.Acquisition;`
- `using HVO.SkyMonitorV5.RPi.Cameras.Projection;`
- `using HVO.SkyMonitorV5.RPi.Options;`
- `using Microsoft.Extensions.Options;`

#### Build Validation

Full solution build now succeeds:
```bash
dotnet build src/HVOv9.sln -c Debug
# Build succeeded in 1.7s (26 projects)
```

## Test Results

### Coverage-Enabled Test Run

Final test results with coverage collection:
- **Total tests**: 432
- **Passed**: 430
- **Skipped**: 2
- **Failed**: 0
- **Duration**: ~12s

### Coverage Artifacts Generated

Cobertura XML files produced for all test projects:
- `HVO.Iot.Devices.Tests/TestResults/*/coverage.cobertura.xml`
- `HVO.RoofControllerV4.RPi.Tests/TestResults/*/coverage.cobertura.xml`
- `HVO.WebSite.Playground.Tests/TestResults/*/coverage.cobertura.xml`
- `HVO.Astronomy.CFITSIO.Tests/TestResults/*/coverage.cobertura.xml`
- `HVO.SkyMonitorV5.RPi.Tests/TestResults/*/coverage.cobertura.xml`

### Test Breakdown by Project

| Project | Passed | Skipped | Notes |
|---------|--------|---------|-------|
| HVO.Iot.Devices.Tests | 128 | 0 | All unit tests passing |
| HVO.RoofControllerV4.RPi.Tests | 67 | 0 | All tests passing |
| HVO.WebSite.Playground.Tests | 102 | 0 | All tests passing |
| HVO.Astronomy.CFITSIO.Tests | Passed | Platform-dependent | Coverage collected |
| HVO.SkyMonitorV5.RPi.Tests | 133 | 2 | 2 MinIO dev-dependent skips |

## Remaining Tasks (Phase 6)

- [ ] Dev container validation (rebuild, verify post-create.sh, tasks, launch)
- [ ] CI/CD validation (push branch, verify workflows, verify artifacts)
- [ ] Configure coverage badge endpoint (requires Shields.io or GitHub Gist setup)

## Remaining Tasks (Phase 7)

- [ ] Update documentation (README, project docs, guides)
- [ ] Final cleanup (remove obsolete files, update .gitignore)
- [ ] Code review preparation (PR description, comparison, breaking changes)

## Files Modified

### Coverage Configuration
- `src/coverage.runsettings` (created)
- `src/HVO.Iot/HVO.Iot.Devices.Tests/HVO.Iot.Devices.Tests.csproj`
- `src/HVO.WebSite/HVO.WebSite.Playground.Tests/HVO.WebSite.Playground.Tests.csproj`
- `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Tests/HVO.SkyMonitorV5.RPi.Tests.csproj`

### Benchmark Fixes
- `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Benchmarks/Benchmarks/EndToEndPipelineBenchmarks.cs`
- `src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Benchmarks/Benchmarks/FrameFilterPipelineBenchmarks.cs`

### CI/CD
- `.github/workflows/dotnet.yml`

### Documentation
- `README.md`
- `docs/projects/repository-reorganization-plan.md`

## Quality Gates

- ✅ Build: PASS (all 26 projects build successfully)
- ✅ Tests: PASS (430/432 tests passed; 2 expected skips)
- ✅ Coverage Collection: PASS (Cobertura XML artifacts generated)
- ✅ Benchmark Compilation: PASS (benchmarks build successfully)

## How to Run Locally

### Run tests with coverage (recommended)
```bash
dotnet test src/HVOv9.sln -c Debug --settings src/coverage.runsettings
```

### Run tests without coverage
```bash
dotnet test src/HVOv9.sln -c Debug
```

### Run domain-specific coverage
```bash
# RoofController V4 with specialized runsettings
dotnet test src/HVO.RoofControllerV4/HVO.RoofControllerV4.sln --settings src/roofcontroller.coverage.runsettings

# Single test project
dotnet test src/HVO.Iot/HVO.Iot.Devices.Tests --settings src/coverage.runsettings
```

### Build benchmarks
```bash
dotnet build src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Benchmarks -c Release
```

### Run benchmarks (smoke test)
```bash
cd src
dotnet run --project HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Benchmarks/HVO.SkyMonitorV5.RPi.Benchmarks.csproj -c Release -- --filter '*EndToEndPipeline*'
```

## Next Steps

1. **Dev Container Validation**: Rebuild the dev container and verify all launch/task configurations work with the new structure
2. **CI/CD Validation**: Push the branch to trigger workflows and verify artifact uploads
3. **Coverage Badge**: Set up a Shields.io endpoint or GitHub Gist to display live coverage percentage
4. **Documentation**: Update all references to old paths in guides and project docs
5. **PR Preparation**: Create comprehensive PR description with before/after comparisons

## Notes

- Coverage percentages may appear low in some test suites because we're collecting across all `[HVO]*` assemblies. Domain-specific runsettings can be created to focus coverage on specific projects for better signal.
- The existing `src/roofcontroller.coverage.runsettings` provides an example of targeted coverage collection for a single domain.
- Benchmark builds are non-blocking in CI (`continue-on-error: true`) until we complete full smoke testing.

## Sign-Off

- ✅ Coverage collection enabled and validated
- ✅ SkyMonitor V5 benchmarks fixed and building
- ✅ All tests passing with coverage artifacts
- ✅ README updated with coverage badge placeholder
- ✅ Documentation updated
