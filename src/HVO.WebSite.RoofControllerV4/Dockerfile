# syntax=docker/dockerfile:1

# -------------------------
# Build the application
# -------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution metadata and project files to warm the restore cache
COPY ["src/global.json", "./"]
COPY ["src/NuGet.config", "./"]
COPY ["src/HVO/HVO.csproj", "HVO/"]
COPY ["src/HVO.Iot.Devices/HVO.Iot.Devices.csproj", "HVO.Iot.Devices/"]
COPY ["src/HVO.WebSite.RoofControllerV4/HVO.WebSite.RoofControllerV4.csproj", "HVO.WebSite.RoofControllerV4/"]
COPY [".LocalPackages/", "/.LocalPackages/"]

# Restore dependencies for the target runtime (linux-arm64 for Raspberry Pi 5)
RUN dotnet restore "HVO.WebSite.RoofControllerV4/HVO.WebSite.RoofControllerV4.csproj" \
    --runtime linux-arm64 \
    --disable-parallel

# Copy the remainder of the source tree and publish the app
COPY src/ ./
WORKDIR /src/HVO.WebSite.RoofControllerV4

RUN dotnet publish "HVO.WebSite.RoofControllerV4.csproj" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained false \
    --output /app/publish

# -------------------------
# Runtime image
# -------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install the minimal native dependencies needed for GPIO/I2C access and health checks
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        libgpiod2 \
        i2c-tools && \
    rm -rf /var/lib/apt/lists/*

# Copy the published binaries from the build stage
COPY --from=build /app/publish .

# Configure ASP.NET Core and logging defaults
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Logging__LogLevel__Default=Information \
    Logging__LogLevel__Microsoft.AspNetCore=Warning \
    TZ=UTC

# Expose the HTTP port used by the site
EXPOSE 8080

# Lightweight health check endpoint
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Run the web site
ENTRYPOINT ["dotnet", "HVO.WebSite.RoofControllerV4.dll"]
