# SkyMonitorV5 Data Store Project Plan

_Last updated: 2025-10-11 (evening)_

> **Status context:** The Frame Context & Rig Integration initiative is complete. This data store project is the next major effort in the SkyMonitor roadmap and should absorb all follow-on storage/configuration items noted in the global TODO catalog.

## Project Goals

- Provide durable on-device storage for SkyMonitorV5 telemetry and operational data using SQLite.
- Consolidate all Entity Framework Core contexts that target SQLite into a dedicated project (`HVO.SkyMonitorV5.Data`).
- Serve as the authoritative store for all customizable observatory configuration: rigs, cameras, optics, filters (global and per-rig), observatory/site metadata, capture pacing, pipeline/stacker options, image encoding preferences, and other runtime knobs currently sourced from JSON.
- Preserve third-party catalog databases (Constellation Lines, HYG v4.2, Deep Sky catalog placeholder, etc.) in their own files while sharing common EF infrastructure.
- Ship repeatable migrations for writable databases so field deployments can be upgraded safely.
- Standardise data placement for container deployments (e.g., `/var/hvo/datastores/`) so Docker volumes can swap or persist DB files easily.

## Deliverables

- New class library `HVO.SkyMonitorV5.Data` with EF Core contexts, entity configurations, seed defaults, and migration history.
- Telemetry database schema and migration set for remote dispatch history, stacker samples, capture pacing logs, and future operational/run-time events (including log viewer support from diagnostics TODOs).
- Configuration schema that persists rigs, cameras, optics, filters, observatory metadata, pipeline/stacker/capture settings, and image encoding profiles with clear versioning/audit metadata.
- Catalog contexts migrated from existing projects and pointed to read-only SQLite files under a shared data directory, including a provisional Deep Sky object catalog under our stewardship until a long-term provider is chosen.
- Service registration extensions (`AddSkyMonitorTelemetryStore`, `AddSkyMonitorConfigurationStore`, etc.) consumed by SkyMonitorV5 runtime to supply strongly-typed options and change tracking.
- Operational docs describing database locations, bootstrapping defaults when the DB is empty, rotation/retention, and container volume mapping.

## Phase Breakdown

### Phase 1 – Project Foundation

- Scaffold `HVO.SkyMonitorV5.Data` project targeting .NET 9.
- Reference `Microsoft.EntityFrameworkCore.Sqlite` and related tooling packages.
- Establish folder layout (`Contexts/`, `Configurations/`, `Migrations/`, `Seed/`).
- Add DI extension methods to register contexts with configurable file paths.
- Decide on default data root (`Data/` under repo, `/var/hvo/datastores/` in containers) and expose via options.

### Phase 2 – Catalog & Configuration Integration

- Relocate existing Constellation Lines / HYG EF models into the data project and add the interim Deep Sky object database we manage until an external source is formalized.
- Model observatory configuration entities (rigs, cameras, optics, filters, image encoding, capture pacing, per-rig overrides) and migrate current JSON defaults into EF seed data.
- Point contexts to their respective SQLite files (`catalogs/constellation.db`, `catalogs/hyg_v42.db`, `catalogs/deep-sky.db`, `configuration/sm-config.db`).
- Ensure catalog contexts run in read-only mode (no migrations) while configuration contexts use migrations and optional version/audit tables.
- Update publish steps to copy catalog DBs and seed configuration snapshots into the runtime data directory and document bootstrap logic when files are absent.

### Phase 3 – Telemetry & Log Persistence

- Design telemetry schema (e.g., `DispatchAttempt`, `DispatchFormatSummary`, `StackerSample`, `CapturePacingSample`, `PipelineTimingSample`).
- Add a structured log/event table to back the forthcoming real-time diagnostics log viewer and historical exports.
- Create initial EF Core migration set for the telemetry database (`telemetry/sm-telemetry.db`).
- Implement retention helpers (rolling purge, optional vacuum) with configurable policies (time- or count-based).
- Persist remote dispatch attempt history (e.g., last N attempts) so UI features pull from the database instead of in-memory buffers.
- Update SkyMonitorV5 services (`DiagnosticsService`, `FrameStateStore`, future log sink) to append finalized samples while keeping in-memory caches for live dashboards.
- Add integration tests covering append/query behaviour, retention jobs, and migration startup.

### Phase 4 – Observability & Operations

- Emit metrics for DB size, row counts, retention jobs, and bootstrap/default seeding operations via diagnostics endpoints.
- Document backup/restore, catalog replacement workflows, and configuration change audit strategies for operators.
- Create Docker volume guidance (sample `docker-compose` snippet mapping host `./data` to container `/var/hvo/datastores`).
- Produce `README` updates and runbooks for migrating existing deployments off JSON configuration into the database.

## Dependencies & Tooling

- Requires EF Core CLI for migrations (`dotnet ef`).
- Relies on existing telemetry models produced in SkyMonitorV5 Phase 3.
- Consumes configuration defaults currently defined in appsettings and options classes; requires mapping strategy for one-time import.
- Assumes container images will be updated post-project to include new data directories.

## Out of Scope / Future Considerations

- Persisting large image assets remains outside this project (still handled by MinIO/S3).
- Multi-station replication or cloud sync is left for a later initiative.
- FITS/TIFF encoder work stays with the core SkyMonitorV5 project but will rely on configuration data housed here when revisited.
- Admin UI for editing configuration is scheduled as part of subsequent UX improvements and will consume the stores built in this project.

## Open Questions

- Exact retention policy (time-based vs fixed-row) needs confirmation before Phase 3 kicks off.
- Do we need a lightweight admin UI to inspect telemetry tables, or are CLI tools sufficient?
- Should catalog DBs be versioned using the same migration mechanism or remain static snapshots?
