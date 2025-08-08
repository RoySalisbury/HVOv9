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
- Dev certificates are provisioned by the dev container automatically (no manual export or `.certs` files required)

### Dev Container details
- Base image: mcr.microsoft.com/devcontainers/dotnet:9.0 (includes .NET 9 SDK)
- VS Code extensions preinstalled:
   - ms-dotnettools.csdevkit (C# Dev Kit)
   - ms-dotnettools.vscode-dotnet-runtime (.NET Runtime)
   - GitHub.remotehub (GitHub Repositories)
   - GitHub.vscode-pull-request-github (GitHub Pull Requests)
   - ms-vsliveshare.vsliveshare (Live Share)
- Forwarded ports: 5136 (HTTP), 7151 (HTTPS)
- Features/Mounts:
   - tailscale feature enabled for Codespaces (ghcr.io/tailscale/codespace/tailscale)
   - Volume mount for X509 stores at /home/vscode/.dotnet/corefx/cryptography/x509stores (persists dev cert store between rebuilds)
- On create, the container runs a script to set up the .NET dev certificate inside the container

### Quick start

1) Open in VS Code (Dev Containers) or GitHub Codespaces.
2) Press F5 and pick “.NET Debug: HVO.WebSite.Playground”.
    - HTTPS: https://localhost:7151
    - HTTP:  http://localhost:5136
    - There’s also “.NET Debug (HTTP only)” to avoid HTTPS entirely.

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
   bash .devcontainer/setup-dotnet-dev-cert.sh
   ```
   Then reload the VS Code window.
- Free ports 5136/7151 if the app can’t bind:
   - VS Code task: “kill:playground”
   - Or run:
      ```bash
      bash .vscode/kill-playground.sh
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

### CI/CD Workflow

The GitHub Actions workflow is split into separate jobs for faster feedback:

- **Build job**: Restores dependencies, builds the solution, and uploads artifacts
- **Unit test jobs**: Run in parallel matrix across all test projects, excluding integration tests
- **Integration test jobs**: Only run when specific conditions are met

#### Integration Test Gate

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

