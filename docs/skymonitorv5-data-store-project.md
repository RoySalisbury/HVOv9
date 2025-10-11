# SkyMonitorV5 Data Store Project Plan

_Last updated: 2025-10-11_

## Project Goals

- Provide durable on-device storage for SkyMonitorV5 telemetry and operational data using SQLite.
- Consolidate all Entity Framework Core contexts that target SQLite into a dedicated project (`HVO.SkyMonitorV5.Data`).
- Preserve third-party catalog databases (Constellation Lines, HYG v4.2, etc.) in their own files while sharing common EF infrastructure.
- Ship repeatable migrations for writable databases so field deployments can be upgraded safely.
- Standardise data placement for container deployments (e.g., `/var/hvo/datastores/`) so Docker volumes can swap or persist DB files easily.

## Deliverables

- New class library `HVO.SkyMonitorV5.Data` with EF Core contexts, entity configurations, and migration history.
- Telemetry database schema and migration set for remote dispatch history, stacker samples, and future operational logs.
- Catalog contexts migrated from existing projects and pointed to read-only SQLite files under a shared data directory.
- Service registration extensions (`AddSkyMonitorTelemetryStore`, etc.) consumed by SkyMonitorV5 runtime.
- Operational docs describing database locations, rotation/retention, and container volume mapping.

## Phase Breakdown

### Phase 1 – Project Foundation

- Scaffold `HVO.SkyMonitorV5.Data` project targeting .NET 9.
- Reference `Microsoft.EntityFrameworkCore.Sqlite` and related tooling packages.
- Establish folder layout (`Contexts/`, `Configurations/`, `Migrations/`, `Seed/`).
- Add DI extension methods to register contexts with configurable file paths.
- Decide on default data root (`Data/` under repo, `/var/hvo/datastores/` in containers) and expose via options.

### Phase 2 – Catalog Integration

- Relocate existing Constellation Lines / HYG EF models into the data project.
- Point contexts to their respective SQLite files (`catalogs/constellation.db`, `catalogs/hyg_v42.db`).
- Ensure catalog contexts run in read-only mode (no migrations) and validate startup health checks.
- Update publish steps to copy catalog DBs into the runtime data directory.

### Phase 3 – Telemetry Persistence

- Design telemetry schema (e.g., `DispatchAttempt`, `DispatchFormatSummary`, `StackerSample`).
- Create initial EF Core migration set for the telemetry database (`telemetry/sm-telemetry.db`).
- Implement retention helpers (rolling purge, optional vacuum).
- Persist remote dispatch attempt history (e.g., last N attempts) so Phase 3 UI/UX features can draw from the database instead of in-memory buffers.
- Update SkyMonitorV5 services (`DiagnosticsService`, `FrameStateStore`) to append finalized samples while keeping in-memory caches for live dashboards.
- Add integration tests covering append/query behaviour and migration startup.

### Phase 4 – Observability & Operations

- Emit metrics for DB size, row counts, and retention jobs via diagnostics endpoints.
- Document backup/restore and catalog replacement workflows for operators.
- Create Docker volume guidance (sample `docker-compose` snippet mapping host `./data` to container `/var/hvo/datastores`).
- Produce `README` updates and runbooks for migrating existing deployments.

## Dependencies & Tooling

- Requires EF Core CLI for migrations (`dotnet ef`).
- Relies on existing telemetry models produced in SkyMonitorV5 Phase 3.
- Assumes container images will be updated post-project to include new data directories.

## Out of Scope / Future Considerations

- Persisting large image assets remains outside this project (still handled by MinIO/S3).
- Multi-station replication or cloud sync is left for a later initiative.
- FITS/TIFF encoder work stays with the core SkyMonitorV5 project.

## Open Questions

- Exact retention policy (time-based vs fixed-row) needs confirmation before Phase 3 kicks off.
- Do we need a lightweight admin UI to inspect telemetry tables, or are CLI tools sufficient?
- Should catalog DBs be versioned using the same migration mechanism or remain static snapshots?
