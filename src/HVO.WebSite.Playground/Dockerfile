# Use the official .NET 9 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Set the working directory inside the container
WORKDIR /src

# Copy solution file and project files for dependency resolution
COPY ["HVO.WebSite.Playground/HVO.WebSite.Playground.csproj", "HVO.WebSite.Playground/"]
COPY ["HVO/HVO.csproj", "HVO/"]
COPY ["HVO.DataModels/HVO.DataModels.csproj", "HVO.DataModels/"]
COPY ["HVO.Iot.Devices/HVO.Iot.Devices.csproj", "HVO.Iot.Devices/"]
COPY ["global.json", "./"]
COPY ["NuGet.config", "./"]

# Restore NuGet packages
RUN dotnet restore "HVO.WebSite.Playground/HVO.WebSite.Playground.csproj"

# Copy the entire source code
COPY . .

# Set working directory to the main project
WORKDIR "/src/HVO.WebSite.Playground"

# Build and publish the application
RUN dotnet publish "HVO.WebSite.Playground.csproj" -c Release -o /app/publish \
    --no-restore \
    --verbosity minimal

# Use the official .NET 9 ASP.NET Core runtime image for the final stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Set the working directory
WORKDIR /app

# Create a non-root user for security
RUN addgroup --system --gid 1001 appgroup && \
    adduser --system --uid 1001 --ingroup appgroup --shell /bin/false appuser

# Copy the published application from the build stage
COPY --from=build /app/publish .

# Set ownership of the app directory to the non-root user
RUN chown -R appuser:appgroup /app

# Switch to the non-root user
USER appuser

# Expose port 8080 (Azure App Service default)
EXPOSE 8080

# Configure ASP.NET Core to listen on port 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Set timezone to UTC
ENV TZ=UTC

# Configure logging
ENV Logging__LogLevel__Default=Information
ENV Logging__LogLevel__Microsoft.AspNetCore=Warning

# Health check endpoint
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Set the entry point
ENTRYPOINT ["dotnet", "HVO.WebSite.Playground.dll"]
