# SkyMonitor v5 Docker Workflow (Raspberry Pi)

_Last updated: 2025-10-11_

This guide explains how to build, validate, and exercise the SkyMonitor v5 Raspberry Pi host inside Docker using the preconfigured `rpi-remote` context on macOS. The workflow mirrors the existing Roof Controller deployment scripts while adding volume checks and an optional runtime soak to observe simulated performance on actual Pi hardware.

## Prerequisites

- Docker CLI 24+ with the `rpi-remote` context pointing at the target Raspberry Pi (`docker context list` should show it).
- Password-less access for the remote context (typically configured with SSH certificates).
- The `HVOv9` repository cloned locally with this branch checked out.
- Optional: ensure the repository scripts directory is on your `$PATH`, or use explicit paths when invoking scripts.

## Building and Testing the Container

Use `scripts/deploy-skymonitor-rpi.sh` to run the full workflow end-to-end. The script:

1. (Optional) Executes the SkyMonitor unit tests—and optionally the benchmark smoke job—inside the Docker build on the Pi. Fails the build if tests fail.
2. Builds the linux/arm64 runtime image using the Dotnet 9 SDK + ASP.NET base images.
3. Creates/updates persistent data directories on the Pi and verifies they are writable via bind mounts.
4. (Optional) Starts the container for a fixed duration so you can watch simulated capture + pipeline logs in real time.

```bash
# Make sure the script is executable (one-time)
chmod +x scripts/deploy-skymonitor-rpi.sh

# Basic usage: run tests and build the runtime image
scripts/deploy-skymonitor-rpi.sh

# Build, run tests, and launch the service for a 2-minute observation window without tailing logs
START_CONTAINER=true RUN_DURATION=120 TAIL_LOGS=false scripts/deploy-skymonitor-rpi.sh
```

### Important Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DOCKER_CONTEXT` | `rpi-remote` | Docker context pointing at the Raspberry Pi. |
| `IMAGE_TAG` | `hvov9/skymonitor-v5:latest` | Name/tag applied to the built image on the remote host. |
| `DATA_ROOT` | `/srv/hvo/skymonitor/datastores` | Root directory on the Pi mapped to `/var/hvo/datastores` inside the container. |
| `RUN_TESTS` | `true` | Set to `false` to skip running the test stage. |
| `RUN_BENCHMARKS` | `false` | Set to `true` to execute the short BenchmarkDotNet smoke job during the test stage. |
| `START_CONTAINER` | `false` | Set to `true` to run the container after build. |
| `RUN_DURATION` | `60` | Seconds to keep the container running when `START_CONTAINER=true`. |
| `HOST_HTTP_PORT` | `5136` | Host port bound to the container’s HTTP endpoint. |
| `HOST_HTTPS_PORT` | `7151` | Host port bound to the container’s HTTPS endpoint. |
| `TAIL_LOGS` | `true` | Set to `false` to skip `docker logs -f` so terminals (including VS Code) stay responsive. |
| `EXTRA_DOCKER_ARGS` | _(empty)_ | Additional options forwarded to `docker run` (e.g., environment variables). |

### Volume Layout

The container defaults to `/var/hvo/datastores` as the SkyMonitor data root. The script provisions two bind mounts so state is persisted on the Pi host:

- `${DATA_ROOT}/configuration` → `/var/hvo/datastores/configuration`
- `${DATA_ROOT}/telemetry` → `/var/hvo/datastores/telemetry`

Catalog assets remain inside the image but can be overridden by mounting a custom `${DATA_ROOT}/catalogs` directory if needed.

## Manual Commands

If you prefer to run individual steps yourself, the commands below mirror the script.

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

# Prepare persistent directories on the Pi
docker --context rpi-remote run --rm \
  -v /srv/hvo/skymonitor/datastores:/datastore \
  alpine:3.20 \
  sh -c 'mkdir -p /datastore/{configuration,telemetry} && chmod -R 775 /datastore'

# Launch the container for ad-hoc testing
docker --context rpi-remote run -d \
  --name hvo-skymonitor-v5 \
  -p 5136:5136 -p 7151:7151 \
  -v /srv/hvo/skymonitor/datastores/configuration:/var/hvo/datastores/configuration \
  -v /srv/hvo/skymonitor/datastores/telemetry:/var/hvo/datastores/telemetry \
  hvov9/skymonitor-v5:latest

# Tail logs and shut down when finished
docker --context rpi-remote logs -f hvo-skymonitor-v5
docker --context rpi-remote stop hvo-skymonitor-v5 && docker --context rpi-remote rm hvo-skymonitor-v5
```

## Benchmark matrix runs

Use `scripts/run-skymonitor-benchmark-matrix.sh` to execute a repeatable set of container scenarios (camera mode × background queue). Key options:

- `DOCKER_CONTEXT`: Override to `hvo-local` for desktop baselines. The script calls `deploy-skymonitor-rpi.sh` to build the image without starting it.
- `DATA_ROOT`: Host path backing `/var/hvo/datastores`; point at a writable folder such as `$(pwd)/benchmarks/<host>/datastore`.
- `RUN_DURATION`: Seconds per scenario (defaults to 1800). Use `RUN_DURATION=120` for quick two-minute soaks.
- `SCENARIO_FILTER`: Comma-separated subset of labels (for example, `SCENARIO_FILTER=mono-bg-on`) when you only need selected runs.
- `CONTAINER_PREFIX`: Helpful when running multiple benches simultaneously; defaults to `hvo-skymonitor-v5-bench`.

Example (local context, single 2-minute scenario that keeps VS Code responsive):

```bash
DOCKER_CONTEXT=hvo-local \
DATA_ROOT="$(pwd)/benchmarks/m2-20251012/datastore" \
RUN_DURATION=120 \
SCENARIO_FILTER=mono-bg-on \
TAIL_LOGS=false \
scripts/run-skymonitor-benchmark-matrix.sh
```

The script archives telemetry/log outputs after each scenario under the bound data root so they can be analyzed with `scripts/analyze_telemetry.py`.

## Next Steps

- When real camera hardware is available again, update `docs/TODO.md` with the latest findings and remove the outstanding hardware validation item once complete.
- Consider wiring the script into CI/CD for automated nightly builds on the observatory Pi fleet.
- Expand the BenchmarkDotNet harness with additional scenarios if the short smoke job surfaces issues under sustained load.
