# Running HVO RoofController V4 in Docker on Raspberry Pi 5

This guide documents how to build and run the `HVO.RoofControllerV4.RPi` web application inside a Docker container on a Raspberry Pi 5. The application interacts with GPIO pins, I²C peripherals, and reads the CPU temperature sensor, so the container must be granted access to those hardware resources.

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

If you have SSH access to the Raspberry Pi, the repo provides `scripts/deploy-roofcontroller-rpi.sh` which builds the image, copies it to the Pi, and restarts the container for you. Set the destination host (and optional overrides) and run the script from the repository root:

```bash
PI_HOST=roofpi.local PI_USER=roy \
  ./scripts/deploy-roofcontroller-rpi.sh
```

Environment variables accepted by the script:

| Variable | Required | Description |
|----------|----------|-------------|
| `PI_HOST` | ✅ | Hostname or IP address of the Raspberry Pi |
| `PI_USER` | ❌ | SSH username (default `pi`) |
| `IMAGE_TAG` | ❌ | Docker image tag to build (default `hvov9/roof-controller:v4`) |
| `CONTAINER_NAME` | ❌ | Container name on the Pi (default `roof-controller`) |
| `HOST_PORT` | ❌ | Host port mapped to container port 8080 (default `8080`) |
| `EXTRA_DOCKER_ARGS` | ❌ | Additional arguments appended to `docker run` (e.g. `-e ASPNETCORE_ENVIRONMENT=Production`) |

The script uses `docker buildx` with `--platform linux/arm64`, copies the image to the Pi via `scp`, removes any existing container with the same name, and launches the updated container with the required GPIO/I²C/thermal device bindings.

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

The application needs access to:


Launch the container with the relevant devices mounted read-only:

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

### Additional runtime tips


## Environment configuration

Environment variables can be supplied at runtime to match your deployment needs. For example:

```bash
docker run -d \
  -p 8080:8080 \
  --device /dev/gpiomem0:/dev/gpiomem \
  --device /dev/i2c-1 \
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


With these steps the RoofController V4 web site runs inside a lightweight container while retaining access to the Raspberry Pi's GPIO, I²C, and thermal telemetry.
