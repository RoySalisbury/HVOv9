# HVOv9 - Hualapai Valley Observatory v9

The ninth version of the Hualapai Valley Observatory software suite: a modern .NET platform for observatory automation, weather APIs, and interactive web control.

## Overview

HVOv9 provides:
- Observatory automation and GPIO device integration
- Weather data APIs and dashboard (Blazor Server)
- Roof controller application
- Solid testing (unit and integration)

## Tech stack

- .NET 9.0
- ASP.NET Core + Blazor Server
- Entity Framework Core
- MSTest-based test projects (with helpers for DI/mocking)
- Scalar for interactive API reference

## Repository layout (high level)

```
src/
├── HVO/                       # Core library (Result<T>, shared types, utilities)
├── HVO.DataModels/            # EF Core DbContext, entities, repositories
├── HVO.NinaClient/            # NINA REST + WebSocket client
├── HVO.SourceGenerators/      # Source generator(s)
├── HVO.Iot.Devices/           # IoT abstractions & implementations
├── HVO.Iot.Devices.Tests/     # IoT tests
├── HVO.WebSite.Playground/    # Web app (Blazor Server + APIs)
└── HVO.WebSite.RoofControllerV4/  # Roof controller app
```

## Dev environment (VS Code + Dev Container)

This repo is configured for VS Code Dev Containers / GitHub Codespaces:
- Dev container installs .NET 9 SDK and helpful extensions
- Ports forwarded by default: 5136 (HTTP) and 7151 (HTTPS)
- VS Code launch profiles auto-build and open the site in your browser

### Quick start

1) Open in VS Code (Dev Containers) or GitHub Codespaces.
2) Press F5 and pick “.NET Debug: HVO.WebSite.Playground”.
    - HTTPS: https://localhost:7151
    - HTTP:  http://localhost:5136
    - There’s also “.NET Debug (HTTP only)” to avoid HTTPS entirely.

Notes
- In Development, HTTPS redirection is disabled by default (configurable).
- LocalApi HttpClient can trust dev certs in Development to avoid SSL errors over port forwarding.

## Build and test

Build everything:
```bash
dotnet build
```

Run tests:
```bash
dotnet test
```

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

Key flags in HVO.WebSite.Playground:
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

## Contributing

1. Follow standards in `.github/copilot-instructions.md`.
2. Include tests with feature changes.
3. Use Result<T> for error-prone operations.
4. Keep components clean (markup in .razor, logic in .razor.cs).

## License

Part of the Hualapai Valley Observatory automation system.

