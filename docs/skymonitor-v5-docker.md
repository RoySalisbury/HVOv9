# SkyMonitor v5 Docker Workflow (Remote Hosts)

_Last updated: 2025-10-12_

This guide explains how to build, validate, and exercise the SkyMonitor v5 container on remote hosts using Docker contexts. It covers both the Raspberry Pi (`rpi-remote`) and the x86/x64 Proxmox node (`hvo-proxmox-home`). The workflow mirrors the existing Roof Controller deployment scripts while adding volume checks and an optional runtime soak to observe simulated performance on actual hardware.

## Prerequisites

- Docker CLI 24+ with the `rpi-remote` and/or `hvo-proxmox-home` contexts configured (`docker context ls` should list them).
- Password-less SSH access for each remote context (typically configured with SSH certificates).
- The `HVOv9` repository cloned locally with this branch checked out.
- Optional: ensure the repository scripts directory is on your `$PATH`, or use explicit paths when invoking scripts.

### Configure Remote Contexts

If a context is missing, create it with the commands below (substitute hostnames as appropriate):

```bash
# Raspberry Pi (arm64)
docker context create rpi-remote \
  --docker "host=ssh://roys@<pi-host>"

# Proxmox x86/x64 node
docker context create hvo-proxmox-home \
  --docker "host=ssh://roys@192.168.2.104"

# Verify contexts are available
docker context ls
```

## Building and Testing the Container

Use `scripts/deploy-skymonitor-rpi.sh` to run the full workflow end-to-end on either architecture. The script:

1. (Optional) Executes the SkyMonitor unit tests—and optionally the benchmark smoke job—inside the Docker build on the remote host. Fails the build if tests fail.
2. Builds the runtime image using the .NET 9 SDK + ASP.NET base images for the host’s architecture.
3. Creates/updates persistent data directories on the remote host and verifies they are writable via bind mounts.
4. (Optional) Starts the container for a fixed duration so you can watch simulated capture + pipeline logs in real time.

```bash
# Make sure the script is executable (one-time)
chmod +x scripts/deploy-skymonitor-rpi.sh

# Basic usage: run tests and build on the Raspberry Pi context
scripts/deploy-skymonitor-rpi.sh

# Build against the Proxmox x86/x64 host
DOCKER_CONTEXT=hvo-proxmox-home \
IMAGE_TAG=hvov9/skymonitor-v5:x64-latest \
DATA_ROOT=/srv/hvo/skymonitor/datastores \
scripts/deploy-skymonitor-rpi.sh

# Build, run tests, and launch for a 2-minute observation window without tailing logs
START_CONTAINER=true RUN_DURATION=120 TAIL_LOGS=false scripts/deploy-skymonitor-rpi.sh
```

### Important Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DOCKER_CONTEXT` | `rpi-remote` | Docker context pointing at the target host (`rpi-remote` for Pi, `hvo-proxmox-home` for x86/x64, `hvo-local-mac` for local validation). |
| `IMAGE_TAG` | `hvov9/skymonitor-v5:latest` | Name/tag applied to the built image on the remote host (override per-architecture if desired). |
| `DATA_ROOT` | `/srv/hvo/skymonitor/datastores` | Root directory on the remote host mapped to `/var/hvo/datastores` inside the container. |
| `RUN_TESTS` | `true` | Set to `false` to skip running the test stage. |
| `RUN_BENCHMARKS` | `false` | Set to `true` to execute the short BenchmarkDotNet smoke job during the test stage. |
| `START_CONTAINER` | `false` | Set to `true` to run the container after build. |
| `RUN_DURATION` | `60` | Seconds to keep the container running when `START_CONTAINER=true`. |
| `HOST_HTTP_PORT` | `5136` | Host port bound to the container’s HTTP endpoint. |
| `HOST_HTTPS_PORT` | `7151` | Host port bound to the container’s HTTPS endpoint. |
| `TAIL_LOGS` | `true` | Set to `false` to skip `docker logs -f` so terminals (including VS Code) stay responsive. |
| `EXTRA_DOCKER_ARGS` | _(empty)_ | Additional options forwarded to `docker run` (e.g., environment variables). |

### Volume Layout

The container defaults to `/var/hvo/datastores` as the SkyMonitor data root. The script provisions bind mounts so state is persisted on the remote host:

- `${DATA_ROOT}/configuration` → `/var/hvo/datastores/configuration`
- `${DATA_ROOT}/telemetry` → `/var/hvo/datastores/telemetry`
- `${DATA_ROOT}/catalogs` → `/var/hvo/datastores/catalogs`
- `${DATA_ROOT}/dataprotection` → `/var/hvo/datastores/dataprotection`
- `${DATA_ROOT}/logs` → `/var/hvo/logs`

Catalog assets remain inside the image but can be overridden by mounting a custom `${DATA_ROOT}/catalogs` directory if needed.

## Manual Commands

If you prefer to run individual steps yourself, the commands below mirror the script. Substitute `--context hvo-proxmox-home` for the x86/x64 host as needed.

```bash
# Build (and run tests) directly on the remote context
DOCKER_BUILDKIT=1 docker --context rpi-remote build \
  --target tests \
  -f src/HVO.SkyMonitorV5.RPi/Dockerfile \
  .

docker --context rpi-remote build \
  -t hvov9/skymonitor-v5:latest \
  -f src/HVO.SkyMonitorV5.RPi/Dockerfile \
  .

# Prepare persistent directories on the remote host
docker --context rpi-remote run --rm \
  -v /srv/hvo/skymonitor/datastores:/datastore \
  alpine:3.20 \
  sh -c 'mkdir -p /datastore/{configuration,telemetry,catalogs,dataprotection,logs} && chmod -R 775 /datastore'

# Launch the container for ad-hoc testing
docker --context rpi-remote run -d \
  --name hvo-skymonitor-v5 \
  -p 5136:5136 -p 7151:7151 \
  -v /srv/hvo/skymonitor/datastores/configuration:/var/hvo/datastores/configuration \
  -v /srv/hvo/skymonitor/datastores/telemetry:/var/hvo/datastores/telemetry \
  -v /srv/hvo/skymonitor/datastores/catalogs:/var/hvo/datastores/catalogs \
  -v /srv/hvo/skymonitor/datastores/dataprotection:/var/hvo/datastores/dataprotection \
  -v /srv/hvo/skymonitor/datastores/logs:/var/hvo/logs \
  hvov9/skymonitor-v5:latest

# Tail logs and shut down when finished
docker --context rpi-remote logs -f hvo-skymonitor-v5
docker --context rpi-remote stop hvo-skymonitor-v5 && docker --context rpi-remote rm hvo-skymonitor-v5
```

## Benchmark Matrix Runs

Use `scripts/run-skymonitor-benchmark-matrix.sh` to execute a repeatable set of container scenarios (camera mode × background queue). Key options:

- `DOCKER_CONTEXT`: Override to `hvo-local-mac` for desktop baselines or `hvo-proxmox-home` for x86/x64 validation. The script calls `deploy-skymonitor-rpi.sh` to build the image without starting it.
- `DATA_ROOT`: Host path backing `/var/hvo/datastores`; point at a writable folder such as `$(pwd)/benchmarks/<host>/datastore`.
- `RUN_DURATION`: Seconds per scenario (defaults to 1800). Use `RUN_DURATION=120` for quick two-minute soaks.
- `SCENARIO_FILTER`: Comma-separated subset of labels (for example, `SCENARIO_FILTER=mono-bg-on`) when you only need selected runs.
- `CONTAINER_PREFIX`: Helpful when running multiple benches simultaneously; defaults to `hvo-skymonitor-v5-bench`.

Example (local context, single 2-minute scenario that keeps VS Code responsive):

```bash
DOCKER_CONTEXT=hvo-local-mac \
DATA_ROOT="$(pwd)/benchmarks/m2-20251012/datastore" \
RUN_DURATION=120 \
SCENARIO_FILTER=mono-bg-on \
TAIL_LOGS=false \
scripts/run-skymonitor-benchmark-matrix.sh
```

The script archives telemetry/log outputs after each scenario under the bound data root so they can be analyzed with `scripts/analyze_telemetry.py`.

## Next Steps

- When real camera hardware is available again, update `docs/TODO.md` with the latest findings and remove the outstanding hardware validation item once complete.
- Consider wiring the script into CI/CD for automated nightly builds across both hardware classes.
- Expand the BenchmarkDotNet harness with additional scenarios if the short smoke job surfaces issues under sustained load.
