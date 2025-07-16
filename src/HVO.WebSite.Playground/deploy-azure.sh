```bash
#!/bin/bash

# Azure Container Registry and App Service deployment script for HVO.WebSite.Playground
# This script builds the Docker image and deploys it to Azure Container Registry and App Service

set -e  # Exit on any error

# Configuration variables
RESOURCE_GROUP="hvo-playground-rg"
LOCATION="East US"
ACR_NAME="hvoacr"
APP_SERVICE_PLAN="hvo-playground-plan"
APP_SERVICE_NAME="hvo-playground-app"
IMAGE_NAME="hvo-playground"
TAG="latest"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check if Azure CLI is installed
check_azure_cli() {
    if ! command -v az &> /dev/null; then
        print_error "Azure CLI is not installed. Please install it first."
        exit 1
    fi
}

# Function to check if Docker is installed
check_docker() {
    if ! command -v docker &> /dev/null; then
        print_error "Docker is not installed. Please install it first."
        exit 1
    fi
}

# Function to login to Azure
azure_login() {
    print_status "Checking Azure login status..."
    if ! az account show &> /dev/null; then
        print_status "Please log in to Azure..."
        az login
    else
        print_status "Already logged in to Azure"
    fi
}

# Function to create resource group
create_resource_group() {
    print_status "Creating resource group: $RESOURCE_GROUP"
    az group create --name $RESOURCE_GROUP --location "$LOCATION" --output table
}

# Function to create Azure Container Registry
create_acr() {
    print_status "Creating Azure Container Registry: $ACR_NAME"
    az acr create --resource-group $RESOURCE_GROUP --name $ACR_NAME --sku Basic --admin-enabled true --output table
}

# Function to build and push Docker image
build_and_push_image() {
    print_status "Building Docker image..."
    
    # Navigate to the project directory
    cd "$(dirname "$0")"
    
    # Build the Docker image
    docker build -t $IMAGE_NAME:$TAG .
    
    # Get ACR login server
    ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --resource-group $RESOURCE_GROUP --query loginServer --output tsv)
    
    # Tag the image for ACR
    docker tag $IMAGE_NAME:$TAG $ACR_LOGIN_SERVER/$IMAGE_NAME:$TAG
    
    # Login to ACR
    print_status "Logging into Azure Container Registry..."
    az acr login --name $ACR_NAME
    
    # Push the image
    print_status "Pushing image to Azure Container Registry..."
    docker push $ACR_LOGIN_SERVER/$IMAGE_NAME:$TAG
    
    print_status "Image pushed successfully: $ACR_LOGIN_SERVER/$IMAGE_NAME:$TAG"
}

# Function to create App Service Plan
create_app_service_plan() {
    print_status "Creating App Service Plan: $APP_SERVICE_PLAN"
    az appservice plan create \
        --name $APP_SERVICE_PLAN \
        --resource-group $RESOURCE_GROUP \
        --is-linux \
        --sku B1 \
        --output table
}

# Function to create App Service
create_app_service() {
    print_status "Creating App Service: $APP_SERVICE_NAME"
    
    # Get ACR login server and credentials
    ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --resource-group $RESOURCE_GROUP --query loginServer --output tsv)
    ACR_USERNAME=$(az acr credential show --name $ACR_NAME --resource-group $RESOURCE_GROUP --query username --output tsv)
    ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --resource-group $RESOURCE_GROUP --query passwords[0].value --output tsv)
    
    # Create the web app
    az webapp create \
        --resource-group $RESOURCE_GROUP \
        --plan $APP_SERVICE_PLAN \
        --name $APP_SERVICE_NAME \
        --deployment-container-image-name $ACR_LOGIN_SERVER/$IMAGE_NAME:$TAG \
        --docker-registry-server-url https://$ACR_LOGIN_SERVER \
        --docker-registry-server-user $ACR_USERNAME \
        --docker-registry-server-password $ACR_PASSWORD \
        --output table
    
    # Configure app settings
    print_status "Configuring app settings..."
    az webapp config appsettings set \
        --resource-group $RESOURCE_GROUP \
        --name $APP_SERVICE_NAME \
        --settings \
            WEBSITES_ENABLE_APP_SERVICE_STORAGE=false \
            ASPNETCORE_ENVIRONMENT=Production \
            WEBSITES_PORT=8080 \
        --output table
}

# Function to enable continuous deployment
enable_continuous_deployment() {
    print_status "Enabling continuous deployment..."
    az webapp deployment container config \
        --resource-group $RESOURCE_GROUP \
        --name $APP_SERVICE_NAME \
        --enable-cd true \
        --output table
}

# Function to show deployment information
show_deployment_info() {
    print_status "Deployment completed successfully!"
    
    # Get the app URL
    APP_URL=$(az webapp show --resource-group $RESOURCE_GROUP --name $APP_SERVICE_NAME --query defaultHostName --output tsv)
    
    echo ""
    echo "=================================="
    echo "Deployment Information"
    echo "=================================="
    echo "Resource Group: $RESOURCE_GROUP"
    echo "Container Registry: $ACR_NAME.azurecr.io"
    echo "App Service: $APP_SERVICE_NAME"
    echo "App URL: https://$APP_URL"
    echo "Health Check: https://$APP_URL/health"
    echo "API Documentation: https://$APP_URL/scalar/v1"
    echo "=================================="
    echo ""
    
    print_status "You can now access your application at: https://$APP_URL"
}

# Main deployment function
main() {
    print_status "Starting Azure deployment for HVO.WebSite.Playground..."
    
    # Check prerequisites
    check_azure_cli
    check_docker
    
    # Azure operations
    azure_login
    create_resource_group
    create_acr
    build_and_push_image
    create_app_service_plan
    create_app_service
    enable_continuous_deployment
    show_deployment_info
    
    print_status "Deployment completed successfully!"
}

# Run the main function
main "$@"
```