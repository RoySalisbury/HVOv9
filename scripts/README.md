# HVOv9 scripts index

This folder contains helper scripts for local dev, deployment, benchmarking, and environment setup across macOS/Linux hosts and Raspberry Pi targets. Use these as building blocks; most are idempotent and safe to re-run.

- Host OS: macOS or Linux for local CLI usage
- Remote targets: Raspberry Pi (64-bit) and other remote Docker engines via SSH
- Dev containers: Scripts are also used from within the VS Code Dev Container

Tip: Source environment loaders in your shell before launching VS Code so secrets propagate into the dev container via remoteEnv.

## Categories

### 1) Deployment (Raspberry Pi)

- deploy-skymonitor-rpi.sh
  - Purpose: Build and deploy SkyMonitor v5 to a remote Pi Docker daemon using a Docker context.
  - Key env vars (required, no defaults to avoid masking issues):
    - DOCKER_CONTEXT, IMAGE_TAG, CONTAINER_NAME
    - DATA_ROOT, EXPORT_ROOT (host directories for persistent data/exports)
    - RUN_TESTS, RUN_BENCHMARKS, START_CONTAINER, RUN_DURATION, HOST_HTTP_PORT, TAIL_LOGS
  - Notes: Auto-detects remote host architecture to select runtime; seeds catalog data on first run; validates writable volumes before optional container start.

- deploy-roofcontroller-rpi.sh
  - Purpose: Build and deploy Roof Controller V4 to a Pi via Docker context.
  - Key env vars:
    - Required: PI_HOST (for status/log output)
    - Optional: DOCKER_CONTEXT (default rpi-remote), IMAGE_TAG (default hvov9/roof-controller:v4), CONTAINER_NAME, HOST_PORT, EXTRA_DOCKER_ARGS
  - Notes: Uses buildx to produce linux/arm64 image and loads it into the remote daemon; maps GPIO/I2C/thermal devices.

### 2) Setup and bootstrap (local/devcontainer)

- setup-ssh.sh
  - Purpose: Provision SSH keys/config to enable ssh:// Docker contexts.
  - Env: HVO_SECRET__SSH__PRIVATE_KEY_B64/PUBLIC_KEY_B64 or raw variants
  - Output: Writes ~/.ssh/id_hvo_docker[.pub], updates ~/.ssh/config, adds to ssh-agent when available.

- setup-docker-contexts.sh
  - Purpose: Ensure Docker CLI contexts exist for: hvo-local-mac, hvo-proxmox-home, rpi-remote.
  - Notes: Idempotent; safe to re-run; contexts use ssh:// endpoints provisioned by setup-ssh.sh.

- setup-dotnet-dev-cert.sh
  - Purpose: Create/trust ASP.NET Core HTTPS dev cert inside the dev container; fixes common ownership issues.

- post-create.sh
  - Purpose: Dev container bootstrapping: apt packages, fonts, dotnet tool workloads, user secrets, SSH, Docker contexts, shell env initialization.
  - Notes: Falls back to load-local-env.sh if advanced secrets aren’t configured.

### 3) Environment and secrets helpers

- setup-local-dev-secrets.sh
  - Purpose: Create .devcontainer/devcontainer.local.env from example and optionally base64-encode your SSH keypair into it.
  - Notes: Guides you to export these values in your macOS shell so the dev container receives them via remoteEnv.

- hvo-env.sh (consolidated)
  - Purpose: Unified environment helper for export/load/auto behaviors.
  - Usage:
    - Export to current shell: `source scripts/hvo-env.sh export`
    - Load with report: `source scripts/hvo-env.sh load`
    - Auto-load on shell startup: post-create configures `.bashrc` to `source scripts/hvo-env.sh auto`
  - Notes: Works inside dev containers, Codespaces, and local shells. The legacy scripts
    `export-local-env.sh`, `load-local-env.sh`, and `init-shell-env.sh` remain as wrappers.

- setup-user-secrets.sh
  - Purpose: Populate dotnet user-secrets for specific projects from environment variables (e.g., DB connections, NINA key, S3 keys).

- setup-codespaces-secrets.sh
  - Purpose: Checklist/helper for setting Codespaces repo secrets via GitHub CLI; prints example commands.

- test-dev-container-env.sh, validate-dev-env.sh
  - Purpose: Smoke and validation checks for dev container env loading and required variables.

### 4) Benchmarks, telemetry, and data utilities

- run-skymonitor-benchmark-matrix.sh
  - Purpose: Execute a matrix of SkyMonitor v5 benchmark scenarios.

- analyze_telemetry.py
  - Purpose: Quick analysis/visualization of telemetry output (used with benchmark results).

- import-constellation-lines.py
  - Purpose: Import constellation line data into catalogs.

- copy-catalog.sh
  - Purpose: Copy catalog assets into the workspace or container as needed (used by post-create.sh).

### 5) iOS (MAUI) developer helpers

- run-maui-ios.sh (consolidated)
  - Purpose: Build, install, and launch on simulator or physical device.
  - Flags: `--mode sim|device`, `--configuration`, `--udid`, `--project`, `--app-id`, `--console`
  - Notes: Legacy scripts `run-roofcontroller-ipad-sim.sh` and `run-roofcontroller-ipad-device.sh` delegate to this.

## Typical flows

- First-time dev container setup (automatic): post-create.sh runs, which sets up dotnet tools, HTTPS dev cert, SSH, Docker contexts, shell env auto-loading, and user-secrets. If you’re working locally, prefer putting secrets in your macOS shell profile and/or use setup-local-dev-secrets.sh + export-local-env.sh.

- Raspberry Pi deployment (SkyMonitor):
  1) Ensure SSH and Docker contexts exist (setup-ssh.sh, setup-docker-contexts.sh)
  2) Set required env vars (DOCKER_CONTEXT, IMAGE_TAG, CONTAINER_NAME, DATA_ROOT, EXPORT_ROOT, etc.)
  3) Run deploy-skymonitor-rpi.sh; optionally set START_CONTAINER=true to launch for a short observation run

- Raspberry Pi deployment (Roof Controller):
  1) Ensure SSH and Docker contexts exist
  2) Set PI_HOST and (optionally) DOCKER_CONTEXT/IMAGE_TAG/HOST_PORT
  3) Run deploy-roofcontroller-rpi.sh

## Platform notes

- macOS ARM64: Supported for local builds and remote Docker contexts via SSH
- Linux x64: Supported for local builds and running inside the dev container
- Raspberry Pi 64-bit: Primary deployment target for SkyMonitor V5 and Roof Controller V4

## Consolidation candidates (not yet changed)

- Environment loaders: export-local-env.sh, load-local-env.sh, init-shell-env.sh could be unified behind a single “hvo-env” loader with subcommands (export/load/auto).
- iPad scripts: The simulator and device runners could be merged with a mode flag to reduce duplication.

If you want me to implement the consolidation, say which path you prefer and I’ll update the scripts + docs accordingly.
