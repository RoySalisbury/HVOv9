# SkyMonitor v5 JSON-to-Database Migration Guide

_Last updated: 2025-10-12_

This guide walks operators through migrating an existing SkyMonitor v5 deployment that still relies on JSON configuration into the new SQLite-backed configuration and telemetry stores. Follow this procedure when upgrading from pre-phase-4 builds or when consolidating legacy `appsettings` overrides into the database.

## Prerequisites

- SkyMonitor v5 build that includes the data-store bootstrapper (post `datastore-phase-3`).
- Shell access to the host running SkyMonitor.
- `sqlite3` CLI installed.
- Backup location (local filesystem or the shared MinIO bucket documented in the operations runbook).
- Change window of ~30 minutes with operator on-call coverage.

## High-Level Steps

1. Back up existing JSON configuration and telemetry artifacts.
2. Capture baseline telemetry/configuration databases before migration.
3. Install the new build and apply EF Core migrations.
4. Run the configuration import tool to hydrate `sm-config.db` from existing JSON.
5. Validate configuration and telemetry snapshots via diagnostics endpoints.
6. Clean up legacy JSON overrides once the database is authoritative.

## Detailed Procedure

### 1. Pre-Migration Backups

- Archive the existing `appsettings.json` and any environment-specific overrides (`appsettings.Production.json`, etc.).
- If the deployment already generated SQLite files under `Data/`, back them up using the nightly backup script from the operations runbook.
- Store the backup artifacts in the designated location (local archive, rsync target, or MinIO bucket).

### 2. Capture Baseline Telemetry Snapshots (Optional)

If the deployment already writes telemetry to SQLite, run:

```bash
sqlite3 sm-telemetry.db ".backup 'sm-telemetry_pre-migration_$(date -u +%Y%m%d).bak'"
```

This snapshot helps compare ingestion metrics before and after the migration.

### 3. Install Updated Build & Apply Migrations

- Deploy the new SkyMonitor v5 build that includes the configuration bootstrapper.
- Run the migration commands from the published binaries:
  ```bash
  dotnet ef database update --context SkyMonitorConfigurationContext
  dotnet ef database update --context SkyMonitorTelemetryContext
  ```
- Verify both commands succeed. If either fails, stop and investigate before moving forward.

### 4. Run Configuration Import

- Execute the `DatabaseBackedConfigurationOptionsConfigurator` import tool (packaged with the runtime) to load JSON configuration into the database:
  ```bash
  dotnet HVO.SkyMonitorV5.Configurator.dll \
    --import-appsettings \
    --source /path/to/appsettings.json \
    --environment Production
  ```
- The tool logs each section it migrates (site metadata, rigs, cameras, optics, filters, telemetry retention policies). Address any reported conflicts before continuing.

### 5. Validate

- Start or restart the SkyMonitor service.
- Hit `/api/v1.0/diagnostics/data-stores` and verify:
  - `Bootstrap.Succeeded=true`
  - Row counts align with the imported configuration entities.
  - Telemetry metrics (`db_size_mb`, `row_count`, `retention_duration_ms`) are reported.
- Visit the UI diagnostics page (once available) to confirm configuration values load from the database.
- Run a capture cycle to ensure telemetry records append successfully.

### 6. Retire Legacy JSON Configuration

- Remove or comment out the migrated sections from `appsettings.json` and environment overrides (keep logging knobs or host-level toggles as documented in the project plan).
- Commit the sanitized JSON files to source control or archive them for reference.

### 7. Update Runbook References

- Record the migration in the site journal with timestamps, operator, and validation results.
- Update any automation scripts or infrastructure-as-code definitions that previously templated JSON configuration to use the database bootstrapper instead.

## Smoke Test Checklist

- [ ] Nightly backup script runs against the new SQLite files and uploads to the designated storage.
- [ ] `/api/v1.0/diagnostics/data-stores` reflects current configuration entities and telemetry metrics.
- [ ] Capture pipeline completes at least one cycle without errors.
- [ ] Operators confirm no stale JSON overrides are still in effect.
- [ ] Dashboard/diagnostics UI (if deployed) displays configuration values from the database.

## Troubleshooting

| Symptom | Possible Cause | Resolution |
| --- | --- | --- |
| Migration tool reports missing tables | Migrations not applied | Re-run EF Core migrations and verify file paths |
| Diagnostics endpoint shows stale JSON data | Service still reading JSON | Ensure JSON sections were removed and service restarted |
| Telemetry gauges missing | Telemetry ingestion service not running | Check service logs and `SkyMonitorTelemetryIngestionService` status |
| Import tool fails on duplicate keys | Conflicting entries in JSON | Resolve duplicates in JSON before rerunning import |

## Post-Migration Follow-Ups

- Schedule quarterly restore rehearsals using the new SQLite backups.
- Rotate MinIO credentials or production S3 credentials once permanent storage providers are in place.
- Tag the repository `datastore-phase-4` after all acceptance criteria are met.

```}