# Docker Build & Deployment Guide

This guide captures the end-to-end workflow for building and running the `HVO.RoofControllerV4.RPi` application in Docker while keeping access to Raspberry Pi hardware peripherals (GPIO, I²C, and temperature telemetry).

> **TL;DR** – the container must either run with `--privileged` **or** receive explicit device mounts for `/dev/i2c-1` and `/dev/gpiomem0` (plus any other sensors you need). Without that access the roof controller will fall back to simulation and the hardware relays will not actuate.

---

## 1. Prerequisites

1. **Enable hardware interfaces on the host Raspberry Pi**
   - Use `raspi-config` (Interfacing Options) or an equivalent tool to enable I²C and GPIO.
   - Reboot to ensure `/dev/i2c-1`, `/dev/gpiomem*`, and the thermal zone files exist.
2. **Install Docker**
   - Follow the [official Docker Engine instructions for Debian/Raspberry Pi](https://docs.docker.com/engine/install/debian/#install-using-the-repository).
   - Add your user to the Docker group and re-login:
     ```bash
     sudo usermod -aG docker $USER
     ```
3. **Clone this repository**
   ```bash
   git clone https://github.com/RoySalisbury/HVOv9.git
   cd HVOv9
   ```

---

## 2. Building the Image

### Native build on Raspberry Pi 5 (recommended)
```bash
cd HVOv9

docker build \
  -f src/HVO.RoofControllerV4.RPi/Dockerfile \
  -t hvov9/roof-controller:v4 \
  .
```

### Cross-build from an x64 workstation with Buildx
```bash
docker buildx build \
  --platform linux/arm64 \
  -f src/HVO.RoofControllerV4.RPi/Dockerfile \
  -t hvov9/roof-controller:v4 \
  .
```
The Dockerfile publishes the ASP.NET Core project for `linux-arm64` and bundles only the runtime bits needed for GPIO & I²C (for example `libgpiod2` and `i2c-tools`).

#### Optional: build directly on the Raspberry Pi from macOS using a remote context
If you prefer to run builds on the Pi while driving commands from your Mac, create a remote Docker context that targets the Pi’s engine over SSH:

```bash
docker context create rpi-remote \
  --docker "host=ssh://<username>@<raspberry-pi-hostname>" \
  --description "Remote engine on Raspberry Pi"

docker context use rpi-remote
```

Once the context is active, any `docker build` or `docker run` you execute from your Mac will run on the Pi. Switch back with `docker context use default` when finished. Ensure SSH keys are configured so the connection is non-interactive.

If you have not configured SSH key-based auth yet, set it up first (Docker’s SSH transport won’t accept password prompts):

```bash
ssh-keygen -t rsa -b 4096

ssh-copy-id <username>@<raspberry-pi-hostname-or-ip>
```

After copying the key you should be able to `ssh <username>@<raspberry-pi-hostname-or-ip>` without entering a password, and the Docker context will connect cleanly.

---

## 3. Deploying with Docker Compose (recommended)

The repository includes `docker-compose.yaml` which codifies the devices, health check, and recommended environment variables. Compose v2 (the `docker compose` plugin) automatically handles the correct specification and no longer requires a `version` field, so the file intentionally omits it.

```bash
# Build the image for the Raspberry Pi profile
docker compose --profile pi build roof-controller

# Start or update the container in the background
docker compose --profile pi up -d roof-controller

# Inspect status and health information
docker compose --profile pi ps
```

Key details:

- The `pi` profile enables device bindings for `/dev/gpiomem0`, `/dev/i2c-1`, and the thermal zone. Leave the profile enabled when deploying to the Raspberry Pi.
- The compose file mirrors the same image tag (`hvov9/roof-controller:v4`) whether you build locally or pull from a registry.
- Compose health checks call `http://localhost:8080/health/live`. If the service enters a `unhealthy` state, inspect logs with `docker compose logs roof-controller`.
- When using a remote Docker context, run the commands exactly as above; Compose will target the active context just like plain Docker commands.

To stop or remove the deployment:

```bash
docker compose --profile pi down

# Or stop without removing the container/image
docker compose stop roof-controller
```

> **Tip:** If you already have a container running from earlier manual `docker run` commands, remove it first (`docker rm -f roof-controller`) so Compose can manage the lifecycle cleanly.

---

## 4. Running the Container Manually (alternative)

If you prefer not to use Compose, the roof controller must still access the Raspberry Pi hardware. You have two options:

### Option A – privileged container (simplest)
```bash
docker run -d \
  --name roof-controller \
  --restart unless-stopped \
  --privileged \
  -p 8080:8080 \
  hvov9/roof-controller:v4
```
`--privileged` grants access to all host devices. This is fast to set up but broader than strictly necessary.

### Option B – minimal device exposure (preferred for least privilege)
```bash
docker run -d \
  --name roof-controller \
  --restart unless-stopped \
  -p 8080:8080 \
  --device /dev/gpiomem0:/dev/gpiomem \
  --device /dev/i2c-1 \
  --mount type=bind,src=/sys/class/thermal/thermal_zone0/temp,dst=/sys/class/thermal/thermal_zone0/temp,readonly \
  hvov9/roof-controller:v4
```
Key device mappings:
- `/dev/gpiomem0` → `/dev/gpiomem` (required for GPIO access).
- `/dev/i2c-1` (default I²C bus for Pi 4/5; change if you use a different bus).
- Thermal telemetry bind to expose CPU temperature (optional but used in diagnostics).

#### Device node notes
- Raspberry Pi 5 exposes `/dev/gpiomem0`, `/dev/gpiomem1`, etc. Use `ls /dev/gpiomem*` to confirm names.
- If your hat lives on another bus (e.g., `/dev/i2c-0`) update the device flag accordingly.
- Keep the container running as root (default) unless you recreate GPIO/I²C group memberships inside the image.

---

## 5. Environment Configuration

The application honors ASP.NET Core’s standard environment variable structure:

```bash
docker run -d \
  --name roof-controller \
  -p 8080:8080 \
  --device /dev/gpiomem0:/dev/gpiomem \
  --device /dev/i2c-1 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Logging__LogLevel__Default=Information \
  -e RoofControllerOptionsV4__IgnorePhysicalLimitSwitches=true \
  hvov9/roof-controller:v4
```

### Hardware detection overrides
If you are running inside a container and need to force physical hardware mode (for example, during testing), the service respects these variables:

- `HVO_FORCE_RASPBERRY_PI=true`
- `HVO_CONTAINER_RPI_HINT=raspberrypi-5`
- `USE_REAL_GPIO=true` (same as `IGpioControllerClient.UseRealHardwareEnvironmentVariable`)

Set them with `-e` flags if auto-detection cannot see the real devices.

---

## 6. Ports & Health Checks

- Container listens on `http://+:8080`.
- Map host ports with `-p HOST:8080`.
- Health endpoints:
  - Liveness: `GET /health/live`
  - Readiness: `GET /health/ready`
  - Detailed: `GET /health`

---

## 7. Updating the Deployment

### Using Docker Compose

```bash
# Rebuild the image (optional if pulling prebuilt tags)
docker compose --profile pi build roof-controller

# Apply updated configuration and restart
docker compose --profile pi up -d roof-controller

# Follow logs after deployment
docker compose logs -f roof-controller
```

Compose will recreate the container only when the image or configuration changes. If you need a clean slate, run `docker compose --profile pi down` first.

### Using manual docker commands

```bash
# Stop and remove the existing container
docker rm -f roof-controller

# Rebuild the image
docker build -f src/HVO.RoofControllerV4.RPi/Dockerfile -t hvov9/roof-controller:v4 .

# Re-run (choose privileged or minimal device approach)
docker run -d --name roof-controller --device /dev/gpiomem0:/dev/gpiomem --device /dev/i2c-1 -p 8080:8080 hvov9/roof-controller:v4
```

For CI/CD scenarios push the tagged image to your registry of choice and pull on the Raspberry Pi before starting the container.

---

## 8. Troubleshooting Checklist

| Symptom | Suggested Checks |
|---------|------------------|
| `System.IO.IOException: Access to the path '/dev/gpiomem' is denied` | Ensure the container runs as root or shares the `gpio` group; confirm `--device /dev/gpiomem0:/dev/gpiomem` or `--privileged` is present. |
| Relay actions are ignored / simulation logs appear | Verify `/dev/i2c-1` was mapped, confirm `USE_REAL_GPIO=true`, and check container logs for `FourRelayFourInputHat initialized ... Mode: Physical I²C`. |
| CPU temperature reads as 0 | Adjust the thermal zone bind mount to match `ls /sys/class/thermal`. |
| Cross-build fails for `linux/arm64` | Confirm Buildx is enabled (`docker buildx ls`) and that the builder supports `linux/arm64`. Build directly on the Pi if necessary. |

---

## 9. Related Documentation

- `docs/roofcontrollerv4-docker.md` – deep dive walkthrough (original notes; kept for historical context).
- `src/HVO.RoofControllerV4.RPi/Documents/HARDWARE_OVERVIEW.md` – hardware wiring and device overview.

Keep this guide up to date whenever Docker build arguments or runtime flags change so deployments stay repeatable.
