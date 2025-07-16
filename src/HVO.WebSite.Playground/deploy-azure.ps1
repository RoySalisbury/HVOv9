# Azure Container Registry and App Service deployment script for HVO.WebSite.Playground
# This script builds the Docker image and deploys it to Azure Container Registry and App Service

param(
    [string]$ResourceGroup = "hvo-playground-rg",
    [string]$Location = "East US",
    [string]$AcrName = "hvoacr",
    [string]$AppServicePlan = "hvo-playground-plan",
    [string]$AppServiceName = "hvo-playground-app",
    [string]$ImageName = "hvo-playground",
    [string]$Tag = "latest"
)

# Function to write colored output
function Write-Status {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARNING] $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

# Function to check if Azure CLI is installed
function Test-AzureCLI {
    try {
        az --version | Out-Null
        return $true
    }
    catch {
        Write-Error "Azure CLI is not installed. Please install it first."
        return $false
    }
}

# Function to check if Docker is installed
function Test-Docker {
    try {
        docker --version | Out-Null
        return $true
    }
    catch {
        Write-Error "Docker is not installed. Please install it first."
        return $false
    }
}

# Function to login to Azure
function Connect-Azure {
    Write-Status "Checking Azure login status..."
    try {
        $account = az account show --query name --output tsv 2>$null
        if ($account) {
            Write-Status "Already logged in to Azure account: $account"
        }
        else {
            Write-Status "Please log in to Azure..."
            az login
        }
    }
    catch {
        Write-Status "Please log in to Azure..."
        az login
    }
}

# Function to create resource group
function New-ResourceGroup {
    Write-Status "Creating resource group: $ResourceGroup"
    az group create --name $ResourceGroup --location $Location --output table
}

# Function to create Azure Container Registry
function New-ContainerRegistry {
    Write-Status "Creating Azure Container Registry: $AcrName"
    az acr create --resource-group $ResourceGroup --name $AcrName --sku Basic --admin-enabled true --output table
}

# Function to build and push Docker image
function Build-AndPushImage {
    Write-Status "Building Docker image..."
    
    # Navigate to the script directory
    $scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
    Set-Location $scriptPath
    
    # Build the Docker image
    docker build -t "${ImageName}:${Tag}" .
    
    # Get ACR login server
    $acrLoginServer = az acr show --name $AcrName --resource-group $ResourceGroup --query loginServer --output tsv
    
    # Tag the image for ACR
    docker tag "${ImageName}:${Tag}" "${acrLoginServer}/${ImageName}:${Tag}"
    
    # Login to ACR
    Write-Status "Logging into Azure Container Registry..."
    az acr login --name $AcrName
    
    # Push the image
    Write-Status "Pushing image to Azure Container Registry..."
    docker push "${acrLoginServer}/${ImageName}:${Tag}"
    
    Write-Status "Image pushed successfully: ${acrLoginServer}/${ImageName}:${Tag}"
}

# Function to create App Service Plan
function New-AppServicePlan {
    Write-Status "Creating App Service Plan: $AppServicePlan"
    az appservice plan create `
        --name $AppServicePlan `
        --resource-group $ResourceGroup `
        --is-linux `
        --sku B1 `
        --output table
}

# Function to create App Service
function New-AppService {
    Write-Status "Creating App Service: $AppServiceName"
    
    # Get ACR login server and credentials
    $acrLoginServer = az acr show --name $AcrName --resource-group $ResourceGroup --query loginServer --output tsv
    $acrUsername = az acr credential show --name $AcrName --resource-group $ResourceGroup --query username --output tsv
    $acrPassword = az acr credential show --name $AcrName --resource-group $ResourceGroup --query passwords[0].value --output tsv
    
    # Create the web app
    az webapp create `
        --resource-group $ResourceGroup `
        --plan $AppServicePlan `
        --name $AppServiceName `
        --deployment-container-image-name "${acrLoginServer}/${ImageName}:${Tag}" `
        --docker-registry-server-url "https://$acrLoginServer" `
        --docker-registry-server-user $acrUsername `
        --docker-registry-server-password $acrPassword `
        --output table
    
    # Configure app settings
    Write-Status "Configuring app settings..."
    az webapp config appsettings set `
        --resource-group $ResourceGroup `
        --name $AppServiceName `
        --settings `
            WEBSITES_ENABLE_APP_SERVICE_STORAGE=false `
            ASPNETCORE_ENVIRONMENT=Production `
            WEBSITES_PORT=8080 `
        --output table
}

# Function to enable continuous deployment
function Enable-ContinuousDeployment {
    Write-Status "Enabling continuous deployment..."
    az webapp deployment container config `
        --resource-group $ResourceGroup `
        --name $AppServiceName `
        --enable-cd true `
        --output table
}

# Function to show deployment information
function Show-DeploymentInfo {
    Write-Status "Deployment completed successfully!"
    
    # Get the app URL
    $appUrl = az webapp show --resource-group $ResourceGroup --name $AppServiceName --query defaultHostName --output tsv
    
    Write-Host ""
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Deployment Information" -ForegroundColor Cyan
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Resource Group: $ResourceGroup"
    Write-Host "Container Registry: $AcrName.azurecr.io"
    Write-Host "App Service: $AppServiceName"
    Write-Host "App URL: https://$appUrl"
    Write-Host "Health Check: https://$appUrl/health"
    Write-Host "API Documentation: https://$appUrl/scalar/v1"
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host ""
    
    Write-Status "You can now access your application at: https://$appUrl"
}

# Main deployment function
function Deploy-ToAzure {
    Write-Status "Starting Azure deployment for HVO.WebSite.Playground..."
    
    # Check prerequisites
    if (-not (Test-AzureCLI)) { return }
    if (-not (Test-Docker)) { return }
    
    try {
        # Azure operations
        Connect-Azure
        New-ResourceGroup
        New-ContainerRegistry
        Build-AndPushImage
        New-AppServicePlan
        New-AppService
        Enable-ContinuousDeployment
        Show-DeploymentInfo
        
        Write-Status "Deployment completed successfully!"
    }
    catch {
        Write-Error "Deployment failed: $_"
        exit 1
    }
}

# Run the main function
Deploy-ToAzure

# Linux/macOS/WSL
./deploy-azure.sh

# Windows PowerShell
.\deploy-azure.ps1