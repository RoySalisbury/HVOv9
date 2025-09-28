# Running HVO RoofController V4 in Docker on Raspberry Pi 5

This guide documents how to build and run the `HVO.WebSite.RoofControllerV4` web application inside a Docker container on a Raspberry Pi 5. The application interacts with GPIO pins, I²C peripherals, and reads the CPU temperature sensor, so the container must be granted access to those hardware resources.

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

## Build the image (linux/arm64)

> If you are already on the Raspberry Pi 5, a standard `docker build` is enough. From an x64 workstation you can cross-build using Docker Buildx.

```bash
# On the Raspberry Pi 5 (native build)
docker build \
  -f src/HVO.WebSite.RoofControllerV4/Dockerfile \
  -t hvov9/roof-controller:v4 \
  .

# From another machine with Buildx enabled (cross-build)
docker buildx build \
  --platform linux/arm64 \
  -f src/HVO.WebSite.RoofControllerV4/Dockerfile \
  -t hvov9/roof-controller:v4 \
  .
```

The Dockerfile publishes the app for the `linux-arm64` runtime and produces a minimal ASP.NET Core runtime image with only the dependencies needed for GPIO and I²C operations (`libgpiod2` and `i2c-tools`).

## Run the container with hardware access

The application needs access to:

- `/dev/gpiomem` for GPIO operations.
- `/dev/i2c-1` (or the appropriate bus) for I²C peripherals.
- `/sys/class/thermal/thermal_zone0/temp` for CPU temperature readings.

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

- If your I²C bus uses a different device (e.g., `/dev/i2c-0`), adjust the `--device` flag accordingly.
- Raspberry Pi 5 exposes multiple memory-mapped GPIO devices (`/dev/gpiomem0`, `/dev/gpiomem1`, ...). Map the one you need into the container—for example `--device /dev/gpiomem0:/dev/gpiomem`—or run `ls /dev/gpiomem*` to confirm the available node names.
- Run the container as root (default) so the process has permission to access the device nodes. If you prefer a non-root user, ensure the container user has the same group IDs as the host `gpio` and `i2c` groups.
- The container exposes port `8080`. Update the `-p` mapping if you need a different host port.
- Health checks hit `http://localhost:8080/health/live`. Confirm this endpoint stays enabled in your deployment configuration.

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
docker build -f src/HVO.WebSite.RoofControllerV4/Dockerfile -t hvov9/roof-controller:v4 .

# Run again with the same device mounts
docker run -d --name roof-controller ... hvov9/roof-controller:v4
```

## Troubleshooting

- **Permission denied for GPIO or I²C**: Confirm the host devices exist (`ls /dev/gpiomem`, `ls /dev/i2c-*`) and that the container is started with the appropriate `--device` flags. Running as root inside the container avoids group mismatches.
- **Missing CPU temperature file**: Some Pi OS builds expose the sensor under a different thermal zone. Run `ls /sys/class/thermal` on the host to find the correct path and adjust the bind mount.
- **Build fails on ARM64 cross-compilation**: Ensure Docker Buildx is enabled (`docker buildx ls`) and the `docker-container` builder supports `linux/arm64`. Alternatively, build directly on the Raspberry Pi.

With these steps the RoofController V4 web site runs inside a lightweight container while retaining access to the Raspberry Pi's GPIO, I²C, and thermal telemetry.
