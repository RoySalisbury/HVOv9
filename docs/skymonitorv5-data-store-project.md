# SkyMonitorV5 Data Store Project Plan

_Last updated: 2025-10-11 (night)_

> **Status context:** The Frame Context & Rig Integration initiative is complete. This data store project is the next major effort in the SkyMonitor roadmap and should absorb all follow-on storage/configuration items noted in the global TODO catalog.
>
> **Execution workflow:** Each phase below should land as an isolated set of commits. When a phase is complete, commit with a detailed message, run the full verification for that phase, push to origin, and create an annotated tag `datastore-phase-{N}` before beginning the next phase. Update this document after each phase to check off completed tasks and capture any findings.

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

### Phase 1 – Project Foundation ☑

- [x] Scaffold `HVO.SkyMonitorV5.Data` project targeting .NET 9.
- [x] Reference `Microsoft.EntityFrameworkCore.Sqlite` and related tooling packages.
- [x] Establish folder layout (`Contexts/`, `Configurations/`, `Migrations/`, `Seed/`, plus foundational `Abstractions/`, `Options/`, `Services/`, and `Extensions/`).
- [x] Add DI extension methods to register contexts with configurable file paths.
- [x] Decide on default data root (`Data/` under repo, `/var/hvo/datastores/` in containers) and expose via options with automatic directory creation.
- [x] Verify the `dotnet-ef` global tool is available; install or update automatically if the exact version required by the solution is missing.

### Phase 2 – Catalog & Configuration Integration ☐

- Migration approach:
	- Move existing catalog DbContexts and entity models into `HVO.SkyMonitorV5.Data/Catalogs/` with namespaces aligning to their folders.
	- Introduce dedicated context types for HYG stars and constellation figures that run against read-only SQLite database files under `catalogs/`.
	- Provide configuration context stubs (`ConfigurationDbContext`) ready to host rig/camera entities with seed defaults sourced from current JSON options.
	- Register catalog contexts via `AddSkyMonitorDataInfrastructure` extensions, using `AddDbContextFactory` where factories are currently consumed.
	- Ensure path provider resolves catalog file locations (`catalogs/hyg_v42.db`, `catalogs/constellation-lines.db`, `catalogs/deep-sky.db`).
		- Interim wiring keeps catalog files at the data-root level (`hyg_v42.sqlite`, `ConstellationLines.sqlite`); move them into explicit `catalogs/` folders once publishing scripts are updated.
- Next steps:
	- [x] Relocate existing Constellation Lines / HYG EF models into the data project and add the interim Deep Sky object database we manage until an external source is formalized. _(Deep sky catalog ships as `catalogs/deep-sky.sqlite` with the full Messier 1–110 set sourced from our CSV seed.)_
	- [x] Model initial observatory configuration entities (site metadata, camera/lens/rig catalogs, capture pipeline, star catalog) in `SkyMonitorConfigurationContext` with EF seed defaults replacing the JSON configuration.
	- [x] Point runtime services to the configuration database via `AddSkyMonitorConfigurationStore` and load host options through the database-backed configurator (removing appsettings bindings).
	- [x] Relocate catalog SQLite assets under `Data/catalogs/` and update publish/build paths so deployments copy the new directory structure.
	- [x] Ensure catalog contexts run in read-only mode (no migrations) while configuration contexts use migrations and optional version/audit tables. _(Catalog registrations now disable migrations and enforce read-only SQL connections while a startup bootstrapper applies the configuration migration set.)_
	- [x] Update publish steps to copy catalog DBs and seed configuration snapshots into the runtime data directory and document bootstrap logic when files are absent. _(Publish output now includes the `Data/catalogs/` and `Data/configuration/` trees, and the bootstrapper migrates/creates `sm-config.db` on first run.)_

### Phase 3 – Telemetry & Log Persistence ☐

- Design telemetry schema (e.g., `DispatchAttempt`, `DispatchFormatSummary`, `StackerSample`, `CapturePacingSample`, `PipelineTimingSample`).
- Add a structured log/event table to back the forthcoming real-time diagnostics log viewer and historical exports.
- Create initial EF Core migration set for the telemetry database (`telemetry/sm-telemetry.db`).
- Implement retention helpers (rolling purge, optional vacuum) with configurable policies (time- or count-based).
- Persist remote dispatch attempt history (e.g., last N attempts) so UI features pull from the database instead of in-memory buffers.
- Update SkyMonitorV5 services (`DiagnosticsService`, `FrameStateStore`, future log sink) to append finalized samples while keeping in-memory caches for live dashboards.
- Add integration tests covering append/query behaviour, retention jobs, and migration startup.

### Phase 4 – Observability & Operations ☐

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

## Notes & Follow-Ups

- UX adjustments (navigation, configuration editors, diagnostics log viewers) are deferred to the dedicated UX project; capture any UX-related ideas here for future triage.
- Record phase-specific lessons learned, tech debt, or toolchain gaps discovered during implementation.
- Phase 1 installed the `dotnet-ef` global tool (9.0.0) to support migrations and seeded the shared data root infrastructure.
- Phase 2 now routes catalog access through `HVO.SkyMonitorV5.Data` with `AddSkyMonitorDataInfrastructure` and read-only SQLite contexts, paving the way for shared configuration and telemetry stores.
- Catalog SQLite payloads ship from `Data/catalogs/` and the SkyMonitor host consumes configuration values exclusively from the new `SkyMonitorConfigurationContext` seeds. The configuration bootstrapper runs EF Core migrations at startup so deployments always receive a seeded `sm-config.db`. Remember to tag the repository (`datastore-phase-2`) once Phase 2 wraps so Phase 3 starts from a clean, tagged baseline.
- EF Core dependencies across the solution (including `dotnet-ef`) have been standardized on 9.0.9 to keep tooling and runtime in lockstep for upcoming migration work.
- Deep sky overlays can now source curated coordinates from the stub catalog while we evaluate third-party catalogs; repository APIs expose magnitude-limited queries on top of the new dataset.
- Deep sky catalog now includes the complete Messier set; the authoritative seed lives in `src/HVO.SkyMonitorV5.Data/Seed/deep-sky-messier.csv` and can be re-imported into `Data/catalogs/deep-sky.sqlite` with `sqlite3 deep-sky.sqlite ".mode csv" ".import --skip 1 ../../../HVO.SkyMonitorV5.Data/Seed/deep-sky-messier.csv deep_sky_object"` (see regeneration notes below).

### Regenerating Catalog Seeds (Phase 2)

- `src/HVO.SkyMonitorV5.Data/Seed/deep-sky-messier.csv` maintains the full Messier 1–110 dataset with J2000 RA/Dec, magnitudes, object types, and optional common names.
- To rebuild `Data/catalogs/deep-sky.sqlite`, create the schema via `sqlite3 deep-sky.sqlite "CREATE TABLE ..."` (see `DeepSkyCatalogContext` for column/ index definitions), run the CSV import, and drop any staging tables once the main table is populated. The documented command above mirrors the process checked in during this phase.
- Include the CSV in release packages so future deployments can recreate the catalog even if the SQLite file is lost or corrupted.
