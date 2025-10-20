# SkyMonitor V5 – Camera Driver Refactor Plan

## Overview

We are eliminating the bespoke "adapter" catalog layer and moving to an attribute-driven camera driver discovery model. Camera definitions will now capture all runtime driver information (identifier, metadata, and optional strongly-typed configuration). Rigs reference cameras + optics as before; the runtime resolves the active driver via reflection-backed discovery and passes any stored driver settings to the adapter implementation.

## Goals

- Discover camera driver implementations automatically via assembly scanning.
- Store driver metadata (id, name, description, version) in code, not configuration tables.
- Allow each driver to declare an optional configuration type for strongly-typed settings serialization.
- Persist driver-specific settings alongside camera definitions in the configuration database.
- Simplify rig/runtime wiring by removing the redundant adapter CRUD surface area.

## Workstreams & Tasks

### 1. Driver Metadata Contract
- [x] Define `CameraDriverAttribute` with properties: `Id`, `DisplayName`, `Description`, `Version`, `ConfigurationType` (nullable).
- [x] Add validation helper to ensure attribute instances supply non-empty `Id` values and optionally provide schema for the UI.
- [x] Document attribute usage and guidelines in this plan + `docs/projects/sky-monitor-v5` README updates.

#### Attribute Usage Quick Reference

```csharp
[CameraDriver(
	id: "Zwo.ASI174MM",
	DisplayName = "ZWO ASI174MM",
	Description = "Monochrome USB3 cooled camera",
	Version = "1.0.0",
	ConfigurationType = typeof(ZwoCameraConfig))]
public sealed class ZwoCameraAdapter : ICameraAdapter
{
	// implementation
}
```

- `Id` must be unique, non-empty, and is trimmed automatically.
- `DisplayName`, `Description`, and `Version` are optional; defaults fall back to the identifier when omitted.
- `ConfigurationType`, when supplied, must be a reference type and represents the strongly-typed settings payload that will be (de)serialized for the driver.
- The static `CameraDriverAttribute.Validate` helper enforces these rules and ensures the decorated class implements `ICameraAdapter`.
- Variations like cooling support or color mode stay in the `CameraSpec`/configuration payload rather than the attribute metadata.
- `CameraSpec.DriverIdentifier` now resolves the runtime driver id (with optional override in catalog/rig options) so downstream services can request adapters directly from the registry.

### 2. Annotate Existing Drivers
- [x] Apply the attribute to `MockCameraAdapter`, `MockColorCameraAdapter`, `ZwoCameraAdapter`, and any other adapters in `HVO.SkyMonitorV5.RPi`.
- [x] Provide accurate metadata strings and, where applicable, point `ConfigurationType` to new option types (e.g., `ZwoCameraConfig`).
- [x] Add unit tests to confirm the attribute data loads correctly for current drivers.

### 3. Runtime Driver Registry
- [x] Implement `CameraDriverRegistry` that scans assemblies for `ICameraAdapter` types decorated with `CameraDriverAttribute`.
- [x] Register the registry as a singleton; expose lookup methods (by id, enumerate descriptors, factory delegate).
- [x] Refactor `CameraDriverFactory` to delegate to the registry instead of the existing `switch`/`if` blocks.
- [x] Log discovery results (count, ids, duplicates) at startup for diagnostics.

### 4. Configuration Model Updates
- [x] Add `DriverSettingsJson` column to `camera_catalog` (migration + entity/DTO updates).
- [x] Update `CameraCatalogEntity` mapping, `CameraCatalogItem` DTOs, and `AllSkyCatalogOptions` projection to include driver settings.
- [x] Provide helper methods to deserialize the JSON into the driver’s `ConfigurationType` when available (with graceful fallback if type not found).
- [x] Seed data updates: ensure mock camera entries include empty JSON blobs.

### 5. API & UI Integration
- [x] Introduce API endpoint (`GET /api/v1.0/configuration/drivers`) exposing available driver descriptors via the registry and align configuration controllers under `configuration/<area>` route prefixes (system, equipment, drivers).
- [x] Update camera create/update DTOs to accept `DriverSettingsJson` payloads.
- [x] Refresh the configuration UI: populate the driver dropdown from the new endpoint; add conditional UI messaging for driver settings via the registry metadata.
- [x] Validate user-provided settings server-side by attempting to deserialize into the declared configuration type.

### 6. Rig & Runtime Simplification
- [x] Remove the adapter catalog entities, controller endpoints, UI tab, and related tests introduced previously.
- [x] Trim `EquipmentConfigurationService` to drop adapter CRUD methods and rely solely on camera/rig operations.
- [x] Keep the existing runtime reload hook but make it trigger on camera/rig changes only (adapter-specific logic becomes unnecessary).

### 7. Testing & Validation
- [ ] Add unit tests for the registry (discovery success, duplicate id detection, configuration type binding).
- [ ] Extend `EquipmentConfigurationService` tests to confirm driver metadata/settings flow end-to-end.
- [ ] Add integration smoke test that the configuration UI returns the dynamic driver list and persists settings.

### 8. Documentation & Migration
- [ ] Write a migration note covering removal of adapter catalog tables and the new camera driver settings column.
- [ ] Update operator runbooks to explain how driver metadata is auto-discovered and how to configure device-specific settings.
- [ ] Provide sample configuration JSON snippets for real hardware (e.g., ZWO device paths, gain defaults).

## Dependencies & Notes

- Ensure the attribute assembly lives in a shared project available to both runtime drivers and the registry (likely `HVO.SkyMonitorV5.RPi`).
- Create the necessary seed data EF logic to populate a blank database for new systems.  We can start the DB from scratch during this plan since there is no need for backwards compatiblity at this point.
- Coordinate UI removal of the adapter tab with API changes to avoid temporary 404s.
- Consider adding a dev-time analyzer to detect duplicate driver IDs at compile time (optional stretch goal).
