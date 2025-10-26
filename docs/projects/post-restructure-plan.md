# Post-Restructure Plan

Status: Open (deferred tasks after repository reorganization)
Owner: HVOv9 Maintainers
Created: 2025-10-25

This plan tracks follow-up work after completing the repository reorganization (Phase 7 Cleanup complete). These tasks are important but not blocking the reorganization merge.

## 1) CI/CD and Workflows

- [ ] Update root workflow triggers for core libraries and shared files
  - Paths: `src/HVO/**`, `src/HVO.DataModels/**`, `src/HVO.SourceGenerators/**`, `src/HVO.WebSite.Themes/**`
  - Shared files: `Directory.Build.props`, `Directory.Packages.props`, `src/global.json`, `src/NuGet.config`
- [ ] Validate all domain workflows on branch (push and verify)
  - Check artifacts (TRX results, coverage) publish correctly
  - Confirm path filters trigger appropriately

## 2) Docker Validation

- [ ] Build all Docker images (RoofController V4, SkyMonitor V5, WebSite)
- [ ] Validate individual containers run correctly
- [ ] Validate full stack via `src/docker-compose.yml`
- [ ] Document any service wiring adjustments if needed

## 3) Dev Container Validation

- [ ] Rebuild the dev container with new paths
- [ ] Verify `post-create.sh` completes successfully
- [ ] Verify solutions open correctly in VS Code
- [ ] Verify tasks and launch configurations work

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
