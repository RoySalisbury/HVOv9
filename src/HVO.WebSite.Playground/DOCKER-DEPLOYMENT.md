# HVO.WebSite.Playground Docker Deployment

This directory contains Docker configuration and deployment scripts for the HVO.WebSite.Playground Blazor Server application, optimized for Azure Container Registry and App Service deployment.

## Files Overview

- `Dockerfile` - Multi-stage Docker build configuration
- `.dockerignore` - Excludes unnecessary files from Docker build context
- `docker-compose.yml` - Local development and testing
- `deploy-azure.sh` - Bash script for Azure deployment
- `deploy-azure.ps1` - PowerShell script for Azure deployment
- `appsettings.Production.json` - Production-optimized configuration

## Prerequisites

### Required Tools
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (for local development)

### Azure Requirements
- Azure subscription
- Resource group (created automatically by deployment script)
- Appropriate permissions to create Azure Container Registry and App Service

## Local Development

### Build and Run Locally

```bash
# Build the Docker image
docker build -t hvo-playground .

# Run the container
docker run -p 8080:8080 hvo-playground

# Or use Docker Compose
docker-compose up --build
```

### Access the Application
- **Application**: http://localhost:8080
- **Health Check**: http://localhost:8080/health
- **API Documentation**: http://localhost:8080/scalar/v1

## Azure Deployment

### Option 1: Automated Deployment (Recommended)

#### Using Bash (Linux/macOS/WSL):
```bash
chmod +x deploy-azure.sh
./deploy-azure.sh
```

#### Using PowerShell (Windows):
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
.\deploy-azure.ps1
```

### Option 2: Manual Deployment

1. **Create Resource Group**
```bash
az group create --name hvo-playground-rg --location "East US"
```

2. **Create Azure Container Registry**
```bash
az acr create --resource-group hvo-playground-rg --name hvoacr --sku Basic --admin-enabled true
```

3. **Build and Push Image**
```bash
# Build the image
docker build -t hvo-playground .

# Login to ACR
az acr login --name hvoacr

# Tag and push
docker tag hvo-playground hvoacr.azurecr.io/hvo-playground:latest
docker push hvoacr.azurecr.io/hvo-playground:latest
```

4. **Create App Service Plan**
```bash
az appservice plan create --name hvo-playground-plan --resource-group hvo-playground-rg --is-linux --sku B1
```

5. **Create App Service**
```bash
az webapp create \
  --resource-group hvo-playground-rg \
  --plan hvo-playground-plan \
  --name hvo-playground-app \
  --deployment-container-image-name hvoacr.azurecr.io/hvo-playground:latest \
  --docker-registry-server-url https://hvoacr.azurecr.io \
  --docker-registry-server-user hvoacr \
  --docker-registry-server-password $(az acr credential show --name hvoacr --query passwords[0].value --output tsv)
```

## Configuration

### Environment Variables

The application supports the following environment variables for Azure deployment:

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
WEBSITES_PORT=8080
WEBSITES_ENABLE_APP_SERVICE_STORAGE=false
```

### Connection Strings

Update the connection string in `appsettings.Production.json` or use Azure App Service configuration:

```bash
az webapp config connection-string set \
  --resource-group hvo-playground-rg \
  --name hvo-playground-app \
  --connection-string-type SQLAzure \
  --settings HualapaiValleyObservatory="Server=tcp:your-server.database.windows.net,1433;Initial Catalog=YourDatabase;..."
```

## Docker Image Details

### Base Images
- **Build Stage**: `mcr.microsoft.com/dotnet/sdk:9.0`
- **Runtime Stage**: `mcr.microsoft.com/dotnet/aspnet:9.0`

### Security Features
- Non-root user (appuser:appgroup)
- Minimal attack surface with runtime-only final image
- Health checks for container orchestration

### Optimization Features
- Multi-stage build for smaller final image
- Layer caching optimization
- Minimal logging in production
- Efficient port configuration (8080)

## Monitoring and Health Checks

### Health Endpoints
- `/health` - Detailed health information
- `/health/ready` - Readiness probe for load balancers
- `/health/live` - Liveness probe for container orchestration

### Application Insights (Optional)
To enable Application Insights monitoring, add the connection string:

```bash
az webapp config appsettings set \
  --resource-group hvo-playground-rg \
  --name hvo-playground-app \
  --settings APPLICATIONINSIGHTS_CONNECTION_STRING="your-connection-string"
```

## Continuous Integration/Deployment

### GitHub Actions Integration
The deployment scripts can be integrated into GitHub Actions workflows:

```yaml
name: Deploy to Azure
on:
  push:
    branches: [ main ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Login to Azure
      uses: azure/login@v1
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS }}
    - name: Deploy to Azure
      run: |
        cd HVO.WebSite.Playground
        ./deploy-azure.sh
```

### Azure DevOps Pipeline
For Azure DevOps integration, use the included scripts in your pipeline:

```yaml
trigger:
- main

pool:
  vmImage: 'ubuntu-latest'

steps:
- script: |
    cd HVO.WebSite.Playground
    chmod +x deploy-azure.sh
    ./deploy-azure.sh
  displayName: 'Deploy to Azure'
```

## Troubleshooting

### Common Issues

1. **Build Failures**
   - Ensure all project references are correctly copied
   - Check that NuGet packages restore successfully
   - Verify .dockerignore doesn't exclude necessary files

2. **Runtime Issues**
   - Check health endpoints: `/health/live`
   - Review application logs in Azure Portal
   - Verify environment variables are set correctly

3. **Database Connection**
   - Ensure firewall rules allow Azure services
   - Verify connection string format
   - Check database credentials

### Debugging Commands

```bash
# Check container logs
docker logs <container-id>

# Connect to running container
docker exec -it <container-id> /bin/bash

# Check Azure App Service logs
az webapp log tail --name hvo-playground-app --resource-group hvo-playground-rg
```

## Resource Cleanup

To remove all Azure resources created by the deployment:

```bash
az group delete --name hvo-playground-rg --yes --no-wait
```

## Performance Considerations

### Resource Sizing
- **Development**: B1 App Service Plan (1 Core, 1.75 GB RAM)
- **Production**: Consider P1V2 or higher for better performance
- **Database**: Ensure adequate DTU/vCore allocation

### Scaling Options
- **Horizontal**: Enable autoscaling in App Service Plan
- **Vertical**: Upgrade to higher-tier App Service Plan
- **Database**: Consider Azure SQL Database scaling options

## Security Best Practices

1. **Use managed identities** instead of connection strings when possible
2. **Enable HTTPS only** in App Service configuration
3. **Configure custom domains** with SSL certificates
4. **Implement Web Application Firewall** for public-facing applications
5. **Regular security updates** for base images and dependencies

## Support

For issues with the Docker deployment:
1. Check the health endpoints first
2. Review Azure App Service logs
3. Verify all configuration settings
4. Contact the HVO development team with specific error messages

---

*This deployment configuration is optimized for Azure App Service with Container Registry integration and follows HVOv9 project standards.*