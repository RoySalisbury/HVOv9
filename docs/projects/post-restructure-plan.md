# Post-Restructure Plan

Status: Open (deferred tasks after repository reorganization)
Owner: HVOv9 Maintainers
Created: 2025-10-25

This plan tracks follow-up work after completing the repository reorganization (Phase 7 Cleanup complete). These tasks are important but not blocking the reorganization merge.

## 1) CI/CD and Workflows

- [x] Update root workflow triggers for core libraries and shared files
  - Paths: `src/HVO/**`, `src/HVO.DataModels/**`, `src/HVO.SourceGenerators/**`, `src/HVO.WebSite.Themes/**`
  - Shared files: `Directory.Build.props`, `Directory.Packages.props`, `src/global.json`, `src/NuGet.config`
  - ✅ Added missing CFITSIO and SkyMonitorV5 test projects to dotnet.yml workflow matrix
- [x] Validate all domain workflows on branch (push and verify)
  - ✅ Tested domain solution builds (Astronomy, IoT) work correctly 
  - ✅ Verified artifact paths generate TRX results in expected locations
  - ✅ Path filters point to existing directories and trigger appropriately

## 2) Docker Validation

- [x] Build all Docker images (RoofController V4, SkyMonitor V5, WebSite)
- [x] Validate individual containers run correctly
- [x] Validate full stack via `src/docker-compose.yml`
- [x] Document any service wiring adjustments if needed
  - ✅ **Complete**: All Docker images build successfully from repository root
  - ✅ **Individual Containers**: RoofController, SkyMonitor, and Website all start correctly with health checks
  - ✅ **Full Stack**: docker-compose orchestration working with proper service dependencies
  - ✅ **Documentation**: Comprehensive validation results in `docs/validation/docker-validation-results.md`
  - 🔧 **Fixed**: WebSite Playground Dockerfile missing HVO.NinaClient dependency
  - ✅ **Resolved**: CFITSIO native libraries now properly configured with libcurl-gnutls dependency

## ✅ Progress Status

- [x] **#3: Dev Container Validation** ✅ *Complete* - Dev container rebuilt and working (user confirmed)
- [x] **#1: CI/CD and Workflows** ✅ *Complete* - Fixed missing test projects and documentation links
  - Added `HVO.Astronomy/HVO.Astronomy.CFITSIO.Tests` and `HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Tests` to both unit and integration test matrices
  - Validated all workflows now passing via GitHub CLI
  - All domain solutions (astronomy.yml, iot.yml, etc.) building successfully
  - Artifact paths generating TRX results correctly
  - **Documentation Links Fixed**: Addressed all 6 issues identified in PR #110 Copilot review:
    - Created missing hardware-simulation-improvements.md guide
    - Added nina-client project documentation directory
    - Created observatory-automation.md architecture document  
    - Added sky-monitor-v5 project documentation
    - Added iot-devices development guide
    - Fixed docs/README.md copilot-instructions path
    - Updated main README coverage badge placeholder

## 4) Deployment Scripts

- [x] Test and validate updated scripts
  - `scripts/deploy-roofcontroller-rpi.sh`
  - `scripts/deploy-skymonitor-rpi.sh`
  - `scripts/run-roofcontroller-ipad-device.sh`
  - `scripts/run-roofcontroller-ipad-sim.sh`
  - `scripts/copy-catalog.sh`
  - ✅ **Complete**: All deployment scripts functional with proper shim delegation
  - ✅ **Validation**: All shims delegate correctly to project-local implementations
  - ✅ **Testing**: Scripts validate arguments and show proper error messages
  - ✅ **Documentation**: Comprehensive validation results in `docs/validation/deployment-scripts-validation-results.md`
  - 🔧 **Fixed**: iOS script permissions issue resolved

## 5) Documentation Polish

- [x] Update `docs/TODO.md` with new paths and reorg completion
- [x] Update `docs/projects/*` where examples reference old paths  
- [x] Update guides (e.g., `docs/guides/blazor-component-best-practices.md`) if any path-sensitive examples exist
  - ✅ **Complete**: All documentation reviewed and updated for new structure
  - ✅ **TODO.md**: Reflects completed reorganization and current project paths
  - ✅ **Project Documentation**: All project-specific docs updated with correct paths
  - ✅ **Guides**: Hardware simulation, dev container, and component guides all use correct paths
  - ✅ **Path Validation**: Verified all referenced paths exist and are accurate

## 6) SkyMonitor V5 Follow-Ups

- [x] Test Docker build for SkyMonitor V5
- [x] Run benchmarks to confirm no regression
  - Store results under `benchmarks/` or `artifacts/benchmarks`
  - ✅ **Complete**: All 19 benchmark scenarios executed successfully with no performance regression
  - ✅ **Docker Build**: Validated in Docker validation phase (569MB image, healthy container)
  - ✅ **Benchmark Results**: Stored in `benchmarks/local-20251026-post-restructure/` with comprehensive analysis
  - ✅ **Performance Analysis**: Frame processing, stacking, filtering, and overlay composition all performing within expected ranges
  - ✅ **Documentation**: Complete validation results in `docs/validation/skymonitor-v5-benchmark-validation-results.md`

## 7) PR & Release (Execution Checklist)

These steps happen outside this doc but are listed for completeness:
- [ ] Create PR from `feature/reorganize-project-structure` to `main`
- [ ] Include summary: before/after structure, CI changes, docs summary
- [ ] Ensure all checks pass (build, tests, coverage)
- [ ] Approvals received
- [ ] Merge PR and tag release if appropriate
- [ ] Update local workspaces and notify contributors

## 8) Future Considerations

- [ ] Evaluate NuGet packaging strategy (domain packages)
- [ ] Consider monorepo tooling (Nuke, Cake, native workspaces)
- [ ] Review integration test structure (centralized vs per-domain)
- [ ] Assess domain workflows for optional publish/pack steps

## Links

- Repository Reorganization Plan (Completed): `docs/projects/repository-reorganization-plan.md`
- Documentation Index: `docs/README.md`
- Root README: `README.md`
