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

- [ ] Build all Docker images (RoofController V4, SkyMonitor V5, WebSite)
- [ ] Validate individual containers run correctly
- [ ] Validate full stack via `src/docker-compose.yml`
- [ ] Document any service wiring adjustments if needed

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

- [ ] Test and validate updated scripts
  - `scripts/deploy-roofcontroller-rpi.sh`
  - `scripts/deploy-skymonitor-rpi.sh`
  - `scripts/run-roofcontroller-ipad-device.sh`
  - `scripts/run-roofcontroller-ipad-sim.sh`
  - `scripts/copy-catalog.sh`

## 5) Documentation Polish

- [ ] Update `docs/TODO.md` with new paths and reorg completion
- [ ] Update `docs/projects/*` where examples reference old paths
- [ ] Update guides (e.g., `docs/guides/blazor-component-best-practices.md`) if any path-sensitive examples exist

## 6) SkyMonitor V5 Follow-Ups

- [ ] Test Docker build for SkyMonitor V5
- [ ] Run benchmarks to confirm no regression
  - Store results under `benchmarks/` or `artifacts/benchmarks`

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
