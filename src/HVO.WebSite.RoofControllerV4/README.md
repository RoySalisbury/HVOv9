# HVO Roof Controller V4

A .NET 9 Blazor Server + ASP.NET Core application that automates the Hualapai Valley Observatory roof. It drives a Lenze SMVector VFD through the Sequent Microsystems 4-Relay/4-Input HAT, exposing a safety-focused REST API, background watchdog service, and modern control UI.

## Highlights
- Safety-first motion sequencing (STOP-first relay logic, watchdog timers, limit switch handling)
- Blazor Server UI with real-time status pill, notifications, and watchdog visuals
- REST API with versioned routing (`/api/v4.0/RoofControl`) for open/close/stop/fault-clear operations
- Structured logging and health checks for observability and rapid diagnostics
- Configurable hardware abstractions, including development bypass for disconnected limit wiring

## Getting Started
1. Install the .NET 9 SDK (see `global.json` for the pinned version).
2. Restore and build:
   ```bash
   dotnet build src/HVO.WebSite.RoofControllerV4/HVO.WebSite.RoofControllerV4.csproj
   ```
3. Run the site (HTTP on :5136 by default):
   ```bash
   dotnet run --project src/HVO.WebSite.RoofControllerV4/HVO.WebSite.RoofControllerV4.csproj
   ```
4. Browse to `http://localhost:5136` for the Blazor UI or call the API endpoints under `/api/v4.0/RoofControl`.

## Documentation
- [Hardware Overview](Documents/HARDWARE_OVERVIEW.md) – wiring, relay/limit mappings, safety philosophy
- [API Reference](Documents/API_REFERENCE.md) – REST endpoints, payloads, and health data
- [Operator Cheat Sheet](Documents/OPERATOR_CHEAT_SHEET.md) – quick reference for field operations
- [Troubleshooting Guide](Documents/TROUBLESHOOTING_GUIDE.md) – symptom → diagnosis mapping
- [Logging Reference](Documents/LOGGING_REFERENCE.md) – structured logging templates and conventions
- Roof diagrams bundle: `Documents/RoofController_Diagrams_2025-09-26.zip`

## Configuration Notes
- Operational settings live in `appsettings*.json` under `RoofControllerOptionsV4` and `RoofControllerHostOptionsV4`.
- `IgnorePhysicalLimitSwitches` is enabled in `appsettings.Development.json` to bypass missing limit wiring during local bench testing. Disable it for real hardware.
- Health check tags: `roof` and `hardware`; see `/health`, `/health/ready`, and `/health/live` for monitoring.

## Testing
Run the dedicated test project to validate relay sequencing, watchdog behaviour, and idempotent command handling:
```bash
dotnet test src/HVO.WebSite.RoofControllerV4.Tests/HVO.WebSite.RoofControllerV4.Tests.csproj
```
