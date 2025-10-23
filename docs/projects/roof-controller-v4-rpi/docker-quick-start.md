# Docker Quick Start - HVO RoofController V4

This guide provides a quick start for deploying the `HVO.RoofControllerV4.RPi` application using the automated deployment script.

> **For comprehensive Docker reference** including manual building, Docker Compose, troubleshooting, and advanced configuration, see [Docker Reference Guide](docker-reference.md).

## Overview

The `HVO.RoofControllerV4.RPi` web application runs in a Docker container on a Raspberry Pi 5. The application interacts with GPIO pins, I²C peripherals, and reads the CPU temperature sensor, so the container must be granted access to those hardware resources.

## Prerequisites

1. **Enable hardware interfaces on the host**
   - Enable I²C and any required GPIO interfaces using `raspi-config` or your preferred configuration tool.
   - Reboot the Pi so `/dev/i2c-1`, `/dev/gpiomem`, and the thermal zone files are available.

2. **Install Docker**
   - Follow Docker's [official Raspberry Pi installation instructions](https://docs.docker.com/engine/install/debian/#install-using-the-repository).
   - Add your user to the `docker` group and re-login: `sudo usermod -aG docker $USER`.

3. **Clone the repository**
   ```bash
   git clone https://github.com/RoySalisbury/HVOv9.git
   cd HVOv9
   ```

## Quick deployment script

If you have a Docker context configured for the Raspberry Pi (for example `rpi-remote` that targets `ssh://pi@roofpi.local`), the repo provides `scripts/deploy-roofcontroller-rpi.sh` which builds the image, streams it to that context, and restarts the container for you. Set the destination host (and optional overrides) and run the script from the repository root:

```bash
PI_HOST=roofpi.local DOCKER_CONTEXT=rpi-remote \
  ./scripts/deploy-roofcontroller-rpi.sh
```

When running from the devcontainer or GitHub Codespaces, provision the SSH key material first via `scripts/setup-ssh.sh`. Configure the `HVO_SECRET__SSH__PRIVATE_KEY_B64` / `HVO_SECRET__SSH__PUBLIC_KEY_B64` secrets so the bootstrap can hydrate `~/.ssh/id_hvo_docker` automatically.

Environment variables accepted by the script:

| Variable | Required | Description |
|----------|----------|-------------|
| `PI_HOST` | ✅ | Hostname or IP address of the Raspberry Pi |
| `DOCKER_CONTEXT` | ❌ | Docker context that points at the Pi daemon (default `rpi-remote`) |
| `IMAGE_TAG` | ❌ | Docker image tag to build (default `hvov9/roof-controller:v4`) |
| `CONTAINER_NAME` | ❌ | Container name on the Pi (default `roof-controller`) |
| `HOST_PORT` | ❌ | Host port mapped to container port 8080 (default `8080`) |
| `EXTRA_DOCKER_ARGS` | ❌ | Additional arguments appended to `docker run` (e.g. `-e ASPNETCORE_ENVIRONMENT=Production`) |

The script uses `docker buildx` with `--platform linux/arm64`, streams the image into the requested Docker context, removes any existing container with the same name, and launches the updated container with the required GPIO/I²C/thermal device bindings. Ensure your Docker context is configured to use the SSH key material provisioned by `scripts/setup-ssh.sh` (stored at `~/.ssh/id_hvo_docker`).

## Build the image manually (linux/arm64)

> If you are already on the Raspberry Pi 5, a standard `docker build` is enough. From an x64 workstation you can cross-build using Docker Buildx.

```bash
# On the Raspberry Pi 5 (native build)
docker build \
  -f src/HVO.RoofControllerV4.RPi/Dockerfile \
  -t hvov9/roof-controller:v4 \
  .

# From another machine with Buildx enabled (cross-build)
docker buildx build \
  --platform linux/arm64 \
  -f src/HVO.RoofControllerV4.RPi/Dockerfile \
  -t hvov9/roof-controller:v4 \
  .
```

The Dockerfile publishes the app for the `linux-arm64` runtime and produces a minimal ASP.NET Core runtime image with only the dependencies needed for GPIO and I²C operations (`libgpiod2` and `i2c-tools`).

## Run the container with hardware access

The application needs direct access to a few device files on the Raspberry Pi:

- `/dev/gpiomem` for high-frequency GPIO operations without root-only privileges.
- `/dev/i2c-1` to communicate with Sequent Microsystems relay/watchdog hats.
- `/sys/class/thermal/thermal_zone0/temp` so the safety interlocks can read the SoC temperature.

Launch the container with the relevant devices mounted read-only:

```bash
docker run -d \
  --name roof-controller \
  --restart unless-stopped \
  -p 8080:8080 \
  --device /dev/gpiomem:/dev/gpiomem \
  --device /dev/i2c-1:/dev/i2c-1 \
  --mount type=bind,src=/sys/class/thermal/thermal_zone0/temp,dst=/sys/class/thermal/thermal_zone0/temp,readonly \
  hvov9/roof-controller:v4
```

### Additional runtime tips

- Set `HVO_FORCE_RASPBERRY_PI=true` when running on non-Pi hardware (or inside CI) to bypass the runtime detection guardrails. Pair it with `HVO_CONTAINER_RPI_HINT=<hostname>` to leave a breadcrumb in structured logs.
- Use the deploy script’s `EXTRA_DOCKER_ARGS` variable to append environment variables or volume bindings without editing the script itself.
- Persist logs by binding `/var/hvo/logs` to a writable host directory: `--mount type=bind,src=/var/hvo/logs,dst=/var/hvo/logs`.
- Drop the `-d` flag during first-run validation so `docker logs -f roof-controller` streams directly into your terminal.


## Environment configuration

Environment variables can be supplied at runtime to match your deployment needs. For example:

```bash
docker run -d \
  -p 8080:8080 \
  --device /dev/gpiomem:/dev/gpiomem \
  --device /dev/i2c-1:/dev/i2c-1 \
  --mount type=bind,src=/sys/class/thermal/thermal_zone0/temp,dst=/sys/class/thermal/thermal_zone0/temp,readonly \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Logging__LogLevel__Default=Information \
  -e RoofControllerOptionsV4__IgnorePhysicalLimitSwitches=true \
  hvov9/roof-controller:v4
```

Any appsettings overrides can follow ASP.NET Core's standard environment-variable syntax.

## Updating the image

When the application changes, rebuild and redeploy:

```bash
# Stop and remove the existing container
docker rm -f roof-controller

# Rebuild
docker build -f src/HVO.RoofControllerV4.RPi/Dockerfile -t hvov9/roof-controller:v4 .

# Run again with the same device mounts
docker run -d --name roof-controller ... hvov9/roof-controller:v4
```

## Troubleshooting

- **Permission denied on GPIO/I²C** – Re-run `raspi-config` to confirm the interfaces are enabled, then reboot. On older images add your SSH user to the `gpio` and `i2c` groups and log out/in.
- **Container cannot open `/dev/i2c-1`** – Verify the host sees the bus with `i2cdetect -y 1`. If it fails, check wiring or hat jumpers.
- **Thermal sensor missing** – Some 64-bit images expose the temp file at `thermal_zone1`. Adjust the bind mount path to match the host output of `ls /sys/class/thermal`.
- **App exits immediately** – Inspect structured logs with `docker logs roof-controller`. Most early exits relate to missing configuration or unavailable GPIO/I²C devices.

With these steps the RoofController V4 web site runs inside a lightweight container while retaining access to the Raspberry Pi's GPIO, I²C, and thermal telemetry.
