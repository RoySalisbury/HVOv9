```instructions
# Copilot Instructions for HVO.SkyMonitorV5.RPi

## Project Overview
SkyMonitor v5 runs on the Raspberry Pi and captures all-sky imagery using either synthetic adapters or physical hardware. The pipeline performs rolling stacking, optional filter overlays, and exposes REST endpoints for frames and configuration updates.

## Key Reminders
- Favor the modular pipeline already in place (camera adapters, exposure controller, stacking engine, filter pipeline). Add new filters under `Pipeline/Filters` and keep them opt-in via configuration.
- Use the centralized documentation under `docs/projects/sky-monitor-v5/` for flow and sequence diagrams, and `docs/sky-monitor-starfield.md` for starfield specifics. Update those assets when behavior or architecture changes.
- Maintain structured logging with `ILogger<T>`; high-frequency capture events should log at `Trace` and configuration changes at `Debug`.
- Preserve the Result<T> pattern for operations surfaced outside the pipeline so controllers and services can translate failures consistently.
- All background thread updates that touch UI components must funnel through `InvokeAsync(StateHasChanged)` when the Blazor dashboard is involved.

## Testing Expectations
- Integration and service tests live in `src/HVO.SkyMonitorV5.RPi.Tests`. Continue using MSTest with the AAA pattern.
- Add benchmarks to `src/HVO.SkyMonitorV5.RPi.Benchmarks` when validating performance-sensitive changes; keep README guidance in sync.
- For new filters or adapters, add focused unit tests plus end-to-end coverage via the existing `FrameFilterPipelineTests` helpers.

## Configuration Patterns
- Respect the strongly typed options classes (`CameraPipelineOptions`, `ObservatoryLocationOptions`). Validate new options via `IValidateOptions<T>` if they can fail startup.
- When extending configuration endpoints (`/api/v1.0/all-sky`), keep payload shapes backward compatible and update the configuration docs.

## Deployment Notes
- Docker support relies on the repo-level scripts. Ensure new dependencies have Raspberry Pi friendly builds and update Dockerfiles when native assets are introduced.
```
