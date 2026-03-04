# HVOv9 - Hualapai Valley Observatory v9

[![.NET Build & Test](https://github.com/RoySalisbury/HVOv9/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/RoySalisbury/HVOv9/actions/workflows/dotnet.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![License](https://img.shields.io/badge/license-proprietary-red)

The ninth version of the Hualapai Valley Observatory software suite. This repo contains **SkyMonitorV5** (production all-sky camera), **Playground CLI tools**, and legacy reference code. Most active projects have been extracted to dedicated repositories.

## 🎯 Overview

HVOv9 is a focused repository containing:
- **SkyMonitorV5** (Production) — High-performance all-sky camera with real-time star detection and cloud coverage analysis
- **NinaClient** — N.I.N.A. API client for imaging session coordination (also published as NuGet from HVO.SDK)
- **Playground CLI / GpioTestApp** — Development utilities and hardware testing
- **SkyMonitorV4** (Deprecated) — Legacy all-sky camera, EOL December 2026

## 📦 Extracted Projects

Many projects have been extracted from this monorepo into dedicated repositories:

| Project | Destination | Notes |
|---------|------------|-------|
| `HVO.Iot.Devices` | [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK) | IoT device abstractions, GPIO control |
| `HVO.Astronomy.CFITSIO` | [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK) | FITS file I/O |
| `HVO.ZWOOptical.ASISDK` | [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK) | ZWO camera SDK wrapper |
| `HVO/` (core library) | [HVO.Core NuGet](https://github.com/RoySalisbury/HVO.SDK) | Result\<T\>, utilities, base types |
| `HVO.SourceGenerators` | [HVO.Core.SourceGenerators NuGet](https://github.com/RoySalisbury/HVO.SDK) | Roslyn source generators |
| `HVO.RoofControllerV4` | [HVO.RoofController](https://github.com/RoySalisbury/HVO.RoofController) | Observatory roof automation |
| `HVO.iOS` (iPad app) | [HVO.RoofController](https://github.com/RoySalisbury/HVO.RoofController) | .NET MAUI iPad app |
| `HVO.WebSite.v9` | [HVO.WebSite](https://github.com/RoySalisbury/HVO.WebSite) | Main observatory dashboard |
| `HVO.WebSite.Themes` | Copied to other repos | Shared UI themes |
| `HVO.TheSkyX` | Deleted | Was a stub/placeholder |

## 🛠️ Tech Stack

- **.NET 10.0** — C# 14, SDK pinned via `global.json`
- **ASP.NET Core + Blazor Server** — Interactive web UI with real-time updates
- **Entity Framework Core** — Data access with SQLite
- **SkiaSharp** — High-performance image processing for star detection
- **MSTest** — Unit and integration testing framework
- **Scalar** — Interactive API documentation

## 📁 Repository Structure

```
src/
├── HVO.NINA/
│   └── HVO.NinaClient/              # NINA API integration
├── HVO.Playground/
│   ├── HVO.Playground.CLI/          # CLI utilities and experiments
│   └── HVO.GpioTestApp/             # Hardware testing tools
├── HVO.SkyMonitorV4/                 # Legacy all-sky camera (deprecated)
│   ├── HVO.SkyMonitorV4.CLI/
│   └── HVO.SkyMonitorV4.RPi/
└── HVO.SkyMonitorV5/                 # Production all-sky camera
    ├── HVO.SkyMonitorV5.Data/       # EF Core DbContext, entities
    ├── HVO.SkyMonitorV5.RPi/        # Blazor dashboard + image processing
    ├── HVO.SkyMonitorV5.RPi.Tests/
    ├── HVO.SkyMonitorV5.RPi.Benchmarks/
    └── HVO.SkyMonitorV5.RPi.Stress/
```

## Hardware driver abstraction pattern (I²C devices)

> **Note**: The I²C device library has been extracted to [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK). This section is retained as reference documentation for the pattern used across HVO projects.

HVOv9 standardizes I²C hardware access around a layered pattern that keeps device logic agnostic of the underlying transport.

- **Register clients** — `II2cRegisterClient` defines byte/word/block read-write helpers; `MemoryI2cRegisterClient` provides a simulation base for tests.
- **Device base class** — `RegisterBasedI2cDevice` owns an `II2cRegisterClient`, exposing protected helpers so concrete drivers focus on domain logic.
- **Concrete drivers and interfaces** — Each device has a DI-friendly contract (e.g., `IFourRelayFourInputHat`, `IWatchdogBatteryHat`).
- **Testing support** — Unit tests use `MemoryI2cRegisterClient` derivatives to simulate register behavior without hardware.

## Dev environment (VS Code + Dev Container)

This repo is configured for VS Code Dev Containers / GitHub Codespaces:
- Dev container installs .NET 10 SDK and helper tooling (Docker CLI, GitHub Copilot, etc.)
- Ports forwarded by default: 5136 (HTTP) and 7151 (HTTPS)
- VS Code launch profiles auto-build and open the site in your browser
- Dev certificates are provisioned by the dev container automatically (no manual export or `.certs` files required)
- Default solution inside the container: `src/HVOv9.sln`

### Dev Container details
- Base image: mcr.microsoft.com/devcontainers/dotnet:10.0 (includes .NET 10 SDK)
- VS Code extensions preinstalled:
   - ms-dotnettools.csdevkit (C# Dev Kit)
   - ms-dotnettools.vscode-dotnet-runtime (.NET Runtime)
   - GitHub.remotehub (GitHub Repositories)
   - GitHub.vscode-pull-request-github (GitHub Pull Requests)
   - ms-vsliveshare.vsliveshare (Live Share)
   - ms-azuretools.vscode-docker (Docker tooling)
   - GitHub.copilot & GitHub.copilot-chat
- Forwarded ports: 5136 (HTTP), 7151 (HTTPS)
- Features/Mounts:
   - tailscale feature enabled for Codespaces (ghcr.io/tailscale/codespace/tailscale)
   - Docker CLI feature (ghcr.io/devcontainers/features/docker-from-docker:1) available for host or remote contexts
   - Volume mount for X509 stores at /home/vscode/.dotnet/corefx/cryptography/x509stores (persists dev cert store between rebuilds)
- On create, the container runs a script to set up the .NET dev certificate inside the container

#### Secrets & SSH bootstrap
- `scripts/setup-user-secrets.sh` hydrates .NET user secrets when the environment provides:
   - `HVO_SECRET__WEBSITEV9__DB_CONNECTION`
   - `HVO_SECRET__WEBSITEPLAYGROUND__DB_CONNECTION`
   - `HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY`
   - `HVO_SECRET__WEBSITEPLAYGROUND__AZDO_PAT`
- `scripts/setup-ssh.sh` provisions SSH keys for Docker contexts (optional env vars):
   - `HVO_SECRET__SSH__PRIVATE_KEY` or `HVO_SECRET__SSH__PRIVATE_KEY_B64`
   - `HVO_SECRET__SSH__PUBLIC_KEY` or `HVO_SECRET__SSH__PUBLIC_KEY_B64`
- Configure these as GitHub repository or Codespaces secrets so they flow into the container automatically. For local VS Code outside the devcontainer, run the scripts manually after exporting the same env vars.
- The Tailscale feature honours the standard `TS_AUTHKEY` environment variable; store it in GitHub secrets to authenticate the tunnel during devcontainer startup.

### Quick start

1) Open in VS Code (Dev Containers) or GitHub Codespaces.
2) Press F5 and pick ".NET Debug: HVO.SkyMonitorV5.RPi".
    - HTTPS: https://localhost:7151
    - HTTP:  http://localhost:5136
    - There’s also “.NET Debug (HTTP only)” to avoid HTTPS entirely.
3) (Optional) Outside the devcontainer, hydrate secrets and SSH keys manually:
   ```bash
   HVO_SECRET__WEBSITEV9__DB_CONNECTION="..." \
   HVO_SECRET__WEBSITEPLAYGROUND__DB_CONNECTION="..." \
   HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY="..." \
   HVO_SECRET__WEBSITEPLAYGROUND__AZDO_PAT="..." \
   bash scripts/setup-user-secrets.sh

   HVO_SECRET__SSH__PRIVATE_KEY_B64="$(base64 ~/.ssh/my_rsa_key | tr -d '\n')" \
   HVO_SECRET__SSH__PUBLIC_KEY_B64="$(base64 ~/.ssh/my_rsa_key.pub | tr -d '\n')" \
   bash scripts/setup-ssh.sh
   ```
   Configure the same values as GitHub repository/Codespaces secrets so the devcontainer picks them up automatically.

   For detailed setup instructions, see [Dev Container Secrets Setup Guide](docs/guides/devcontainer-secrets-setup.md).

Notes
- In Development, HTTPS redirection is disabled by default (configurable).
- LocalApi HttpClient can trust dev certs in Development to avoid SSL errors over port forwarding.
- Dev certs are container-managed; you don’t need to run any setup scripts or keep a local PFX.

### Troubleshooting the Dev Container
- Rebuild the container (fixes most environment drift):
   - VS Code: Command Palette → “Dev Containers: Rebuild Container”
   - GitHub Codespaces: Use the “Rebuild Container” action from the codespace menu
- Re-run dev cert setup if HTTPS fails to start:
   ```bash
   bash scripts/setup-dotnet-dev-cert.sh
   ```
   Then reload the VS Code window.
- Free ports 5136/7151 if the app can’t bind:
   - VS Code task: "kill:skymonitorv5"
   - Or run:
      ```bash
      bash .vscode/kill-skymonitorv5.sh
      ```
- Reset build state if restores/builds start failing:
   ```bash
   dotnet restore --force
   dotnet clean
   dotnet build
   ```
- Local Docker only (not Codespaces): clear persisted X509 store if certs get stuck:
   ```bash
   docker volume rm x509stores
   ```

## Build and test

Build everything:
```bash
dotnet build
```

Run tests:
```bash
dotnet test
```

## Docker deployment

- [SkyMonitor v5 Docker guide](docs/skymonitor-v5-docker.md) – covers building locally or against the Pi. When launching from VS Code terminals, export `TAIL_LOGS=false` to avoid blocking while the container runs.
- [SkyMonitor v5 Operations Runbook](docs/skymonitor-v5-operations-runbook.md) – backup cadence, restore rehearsals, catalog swaps, and change-control guidance for ops teams.
- [SkyMonitor v5 JSON Migration Guide](docs/skymonitor-v5-json-migration-guide.md) – step-by-step workflow for moving legacy appsettings configuration into the new SQLite stores.

### CI/CD Workflow

The GitHub Actions workflow is split into separate jobs for faster feedback:

- **Build job**: Restores dependencies, builds the solution, and uploads artifacts
- **Unit test jobs**: Run in parallel matrix across all test projects, excluding integration tests
- **Integration test jobs**: Only run when specific conditions are met

#### Integration Test Gate (labels or schedule)

Integration tests are slower and may require special setup (like GPIO hardware simulation). They only run when:

- **On main branch**: All pushes to main automatically run integration tests
- **Scheduled runs**: Nightly at 2 AM UTC via cron schedule
- **PR with label**: Add the `integration-tests` label to any PR to include integration tests

For most PRs, only unit tests run by default, providing faster feedback. Add the `integration-tests` label when you need full test coverage.

Example: Adding the integration-tests label to a PR:
```bash
# Using GitHub CLI
gh pr edit --add-label "integration-tests"
```

### CI Automation
- Test artifacts:
   - Both unit and integration jobs publish TRX results per project as run artifacts.
   - Download from the run page to inspect failing tests locally.

## API docs

- OpenAPI JSON:  /openapi/v1.json
- Scalar UI:     /scalar/v1 (Development only)

Example requests (Development defaults):
```bash
curl http://localhost:5136/api/v1.0/weather/latest
curl "http://localhost:5136/api/v1.0/weather/highs-lows?startDate=2025-07-01&endDate=2025-07-13"
```

## Configuration

App settings are in `appsettings.json` with environment overrides, e.g. `appsettings.Development.json`.

Key flags in HVO.SkyMonitorV5.RPi:
- `EnableHttpsRedirect` (bool)
   - Default: true (non-Development), false (Development)
   - Controls UseHttpsRedirection()
- `TrustDevCertificates` (bool)
   - Default: true (Development), false (non-Development)
   - When true, LocalApi HttpClient accepts the local dev cert

Ports:
- HTTP 5136, HTTPS 7151 (configurable via ASPNETCORE_URLS in launch)

Database:
- Store connection strings securely (user secrets, environment variables). Avoid embedding secrets in source.

## Coding standards

See `.github/copilot-instructions.md` for workspace-wide standards:
- Explicit Program.Main (no top-level statements)
- Code-behind for Razor components (`.razor.cs`), scoped CSS/JS
- Result<T> pattern for operations that can fail
- Structured logging with ILogger<T>
- API versioning via URL segments (`/api/v1.0/...`)
- Keep validation inside request DTOs using data annotations or `IValidatableObject` so controllers rely on automatic model validation

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development workflow, branch naming, and PR process.

## Future enhancements

### SkyMonitor V5 FITS Export - Optional enhancements

The core FITS export implementation is complete (Phases 1-8). The following optional items are available for future development:

#### Admin UI for FITS Configuration
- **Route**: `/admin/settings/fits-export`
- **Purpose**: Form-based interface for managing FITS export settings
- **Features**:
  - Form fields for all 6 options (EnableForRaw, EnableForProcessed, BitDepth, UnsignedU16, Compression, WriteChecksum)
  - Validation consistent with data annotations
  - Save via `SystemConfigurationService` PUT endpoint
- **Status**: Deferred - API endpoints are fully functional; UI is optional convenience

#### Database Seeding for FITS Defaults
- **Purpose**: Pre-populate SystemSettings table with initial FITS configuration values during bootstrap
- **Status**: Not required - service returns sensible defaults when no DB row exists (revision=0)

#### Color-preserving FITS
- **Feature**: Write NAXIS=3 color cube (R,G,B planes) instead of grayscale for color sensors
- **Use case**: Preserve full color information for RGB camera sensors
- **Status**: Enable after core FITS delivery is stable

#### Tiled Compression Tuning
- **Feature**: Expose tile dimension options; call `fits_set_tile_dimll` if available in build
- **Use case**: Fine-tune compression performance and quality
- **Status**: Advanced feature for performance optimization

#### Advanced WCS / Plate Solve Integration
- **Feature**: Write full TAN/SIP WCS including distortion terms (PV/SIP coefficients)
- **Use case**: Advanced astrometric solutions with distortion correction
- **Status**: Requires plate solving integration

#### Multi-extension Archival FITS
- **Feature**: Package RAW and PROCESSED as separate HDUs (MEF) for archival backends
- **Use case**: Single-file archival format containing multiple image versions
- **Status**: Advanced archival feature

#### Format Negotiation & Policy
- **Feature**: Per-sink overrides (e.g., S3 compressed, filesystem uncompressed), API Accept negotiation
- **Use case**: Different export formats for different storage backends
- **Status**: Advanced configuration feature

#### Archive Backfill Tools
- **Feature**: Batch migrate legacy PNG/JPEG/skimg to FITS with metadata stamping
- **Use case**: Convert existing image archives to FITS format
- **Status**: Operational tooling for migration scenarios

#### Performance & Throughput
- **Feature**: Benchmarks for compression choices; consider streaming writer for very large frames
- **Use case**: Optimize FITS export performance for high-throughput scenarios
- **Status**: Performance optimization after baseline is stable

## License

Proprietary — see [LICENSE](LICENSE).

