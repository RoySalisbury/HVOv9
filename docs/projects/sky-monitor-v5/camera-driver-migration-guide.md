# Camera Driver Refactor Migration Guide

_Last updated: 2025-10-20_

This guide walks through the data-store changes introduced by the SkyMonitor v5 camera driver refactor. The refactor replaces the adapter catalog tables with attribute-driven discovery and stores per-driver settings alongside camera definitions.

## Summary

| Change | Description |
| --- | --- |
| Driver discovery | Camera driver implementations are discovered at startup via the `CameraDriverAttribute`. No catalog seed data is required. |
| Configuration storage | Camera-specific driver settings are persisted in the `camera_catalog` table (`driver_settings_json` column). |
| Removed tables | `camera_adapter_catalog`, `camera_adapter_metadata`, and related join tables have been removed. |
| API updates | A new endpoint `GET /api/v1.0/configuration/drivers` exposes the runtime driver descriptors (id, display name, configuration metadata). |

## Prerequisites

- SkyMonitor v5 build that includes the camera driver refactor migrations.
- Recent backups of the configuration data store (`sm-config.db`).
- Entity Framework Core CLI (`dotnet ef`) installed in the environment running the migration.

## Migration Steps

1. **Back up the configuration store**
   ```bash
   cd /var/hvo/datastores/configuration
   sqlite3 sm-config.db ".backup 'sm-config_pre-driver-refactor_$(date -u +%Y%m%d).bak'"
   ```

2. **Apply entity framework migrations**
   ```bash
   cd /opt/hvo/skymonitor
   dotnet ef database update --context SkyMonitorConfigurationContext
   ```
   This adds the `driver_settings_json` column to `camera_catalog` and removes the obsolete adapter catalog tables.

3. **Verify schema state**
   ```bash
   sqlite3 sm-config.db ".schema camera_catalog"
   sqlite3 sm-config.db "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'camera_adapter%';"
   ```
   The `camera_catalog` schema should include `driver_settings_json TEXT`. No `camera_adapter_*` tables should remain.

4. **Hydrate driver settings (optional)**
   If previous deployments stored driver settings in external files or operator notes, copy them into the new column. For example, to assign ZWO defaults:
   ```sql
   UPDATE camera_catalog
      SET driver_settings_json = json_object(
          'usbLimit', 40,
          'defaultGain', 150,
          'cooler', json_object('enabled', 1, 'setPointCelsius', -10)
      )
    WHERE key = 'MockASI174MM';
   ```

5. **Restart SkyMonitor or trigger configuration reload**
   ```bash
   systemctl restart hvo-skymonitor.service
   # or
   curl -X POST http://localhost:5136/api/v1.0/configuration/reload
   ```

6. **Validate runtime discovery**
   ```bash
   curl http://localhost:5136/api/v1.0/configuration/drivers | jq
   sqlite3 sm-config.db "SELECT key, driver_identifier, driver_settings_json FROM camera_catalog ORDER BY key;"
   ```
   Ensure the expected drivers are listed and JSON payloads appear for cameras that require configuration.

## Rollback

1. Stop the SkyMonitor service.
2. Restore the pre-refactor backup of `sm-config.db`.
3. Re-run `dotnet ef database update --context SkyMonitorConfigurationContext` using the earlier build version (before the refactor).
4. Start the service and validate the configuration API.

## Operational Notes

- Registry discovery is logged at application startup (`CameraDriverRegistry`). Duplicate identifiers are reported as warnings.
- The UI and API expose a `SupportsConfiguration` flag per driver. Operators can inspect the expected configuration type via `ConfigurationType` in the driver descriptor payload.
- Future migrations may introduce strongly-typed configuration classes for hardware adapters (for example, richer ZWO options). Keep JSON payloads minimal to ease future schema upgrades.
