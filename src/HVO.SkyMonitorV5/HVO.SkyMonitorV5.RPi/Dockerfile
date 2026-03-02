# syntax=docker/dockerfile:1

# ---------------------------------
# Restore dependencies and prepare build assets
# ---------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS restore
ARG SKYMONITOR_RUNTIME=linux-arm64
WORKDIR /src

# Ensure native dependencies for SkiaSharp and telemetry are present during restore/test stages
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    fontconfig \
    fonts-dejavu-core \
    fonts-liberation2 \
    fonts-noto-core \
    fonts-noto-mono \
    fonts-noto-color-emoji \
    fonts-roboto \
    fonts-open-sans \
    libuuid1 \
    libfontconfig1 \
    libfreetype6 \
    libharfbuzz0b \
    libpng16-16 \
    libjpeg62-turbo \
    libexpat1 && \
    rm -rf /var/lib/apt/lists/* && \
    fc-cache -f

# Copy solution-level metadata and project files to maximize cache hits
COPY ["src/global.json", "./"]
COPY ["src/NuGet.config", "./"]
COPY ["Directory.Build.props", "../"]
COPY ["Directory.Packages.props", "../"]
COPY ["src/HVO/HVO.csproj", "HVO/"]
COPY ["src/HVO.WebSite.Themes/HVO.WebSite.Themes.csproj", "HVO.WebSite.Themes/"]
COPY ["src/HVO.Astronomy/HVO.Astronomy.CFITSIO/HVO.Astronomy.CFITSIO.csproj", "HVO.Astronomy/HVO.Astronomy.CFITSIO/"]
COPY ["src/HVO.Astronomy/HVO.Astronomy.CFITSIO.NativeAssets/HVO.Astronomy.CFITSIO.NativeAssets.csproj", "HVO.Astronomy/HVO.Astronomy.CFITSIO.NativeAssets/"]
COPY ["src/HVO.ZWOOptical/HVO.ZWOOptical.ASISDK/HVO.ZWOOptical.ASISDK.csproj", "HVO.ZWOOptical/HVO.ZWOOptical.ASISDK/"]
COPY ["src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.Data/HVO.SkyMonitorV5.Data.csproj", "HVO.SkyMonitorV5/HVO.SkyMonitorV5.Data/"]
COPY ["src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/HVO.SkyMonitorV5.RPi.csproj", "HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/"]
COPY ["src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Tests/HVO.SkyMonitorV5.RPi.Tests.csproj", "HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Tests/"]
COPY ["src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Benchmarks/HVO.SkyMonitorV5.RPi.Benchmarks.csproj", "HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Benchmarks/"]

RUN dotnet restore "HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi/HVO.SkyMonitorV5.RPi.csproj" \
    --runtime ${SKYMONITOR_RUNTIME} \
    --disable-parallel

# Bring in the remaining source tree for downstream stages
COPY src/ ./

# ---------------------------------
# Test stage (optional)
# ---------------------------------
FROM restore AS tests
ARG RUN_BENCHMARKS=false

RUN dotnet test \
    HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Tests/HVO.SkyMonitorV5.RPi.Tests.csproj \
    --configuration Release \
    --logger "trx;LogFileName=SkyMonitorV5.Tests.trx"

RUN if [ "$RUN_BENCHMARKS" = "true" ]; then \
    dotnet run \
    --project HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Benchmarks/HVO.SkyMonitorV5.RPi.Benchmarks.csproj \
    --configuration Release \
    -- \
    --job short \
    --filter "Category=Smoke"; \
    else \
    echo "Skipping benchmark smoke run"; \
    fi

# ---------------------------------
# Publish stage
# ---------------------------------
FROM restore AS publish
ARG SKYMONITOR_RUNTIME=linux-arm64
WORKDIR /src/HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi

RUN dotnet publish "HVO.SkyMonitorV5.RPi.csproj" \
    --configuration Release \
    --runtime ${SKYMONITOR_RUNTIME} \
    --self-contained false \
    --output /app/publish

# ---------------------------------
# Runtime image
# ---------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
ARG BUILD_VERSION=dev
WORKDIR /app

# Install native dependencies required by camera adapters, telemetry, SkiaSharp, and CFITSIO
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    libcurl3-gnutls \
    fontconfig \
    fonts-dejavu-core \
    fonts-liberation2 \
    fonts-noto-core \
    fonts-noto-mono \
    fonts-noto-color-emoji \
    fonts-roboto \
    fonts-open-sans \
    libuuid1 \
    libgpiod2 \
    i2c-tools \
    libfontconfig1 \
    libfreetype6 \
    libharfbuzz0b \
    libpng16-16 \
    libjpeg62-turbo \
    libexpat1 && \
    rm -rf /var/lib/apt/lists/* && \
    fc-cache -f

# Copy the published binaries from the publish stage
COPY --from=publish /app/publish .

# Seed the data-store root with the bundled catalogs while keeping configuration/telemetry writable
RUN mkdir -p /var/hvo/datastores && \
    cp -rT ./Data /var/hvo/datastores && \
    mkdir -p /var/hvo/datastores/configuration /var/hvo/datastores/telemetry /var/hvo/datastores/dataprotection && \
    mkdir -p /var/hvo/logs && \
    mkdir -p /var/hvo/exports/raw /var/hvo/exports/processed && \
    chmod -R 775 /var/hvo/datastores && \
    chmod -R 775 /var/hvo/logs && \
    chmod -R 775 /var/hvo/exports

ENV ASPNETCORE_URLS="http://+:5136" \
    ASPNETCORE_ENVIRONMENT=Production \
    Logging__LogLevel__Default=Information \
    Logging__LogLevel__Microsoft.AspNetCore=Warning \
    SkyMonitor__Data__PreferContainerRoot=true \
    SkyMonitor__Logging__Directory=/var/hvo/logs \
    SkyMonitor__Logging__MaxFileSizeMB=10 \
    SkyMonitor__Logging__MaxRetainedFiles=5 \
    HVO_BUILD_VERSION=${BUILD_VERSION} \
    TZ=UTC

EXPOSE 5136

HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 \
    CMD curl -f http://localhost:5136/health/live || exit 1

ENTRYPOINT ["dotnet", "HVO.SkyMonitorV5.RPi.dll"]
