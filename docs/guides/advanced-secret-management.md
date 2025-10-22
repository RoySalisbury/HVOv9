# Advanced Secret Management for HVO Dev Containers

This document describes advanced secret management options for HVO dev containers, providing secure alternatives to storing secrets in local files.

## Overview

The HVO dev container supports multiple secret management approaches:

1. **Local Environment File** (current default)
2. **Azure Key Vault** (recommended for Azure users)
3. **AWS Secrets Manager** (recommended for AWS users)
4. **macOS Keychain** (for local Mac development)
5. **HashiCorp Vault** (for enterprise environments)

## Quick Setup Options

### Option 1: Azure Key Vault (Recommended)

**Setup:**
```bash
# 1. Create Azure Key Vault
az keyvault create --name "hvo-secrets" --resource-group "your-rg" --location "eastus"

# 2. Store secrets
az keyvault secret set --vault-name "hvo-secrets" --name "hvo-ssh-private-key" --value "$(base64 -i ~/.ssh/your_key)"
az keyvault secret set --vault-name "hvo-secrets" --name "hvo-ssh-public-key" --value "$(base64 -i ~/.ssh/your_key.pub)"

# 3. Set environment variable for dev container
export HVO_AZURE_KEYVAULT_NAME="hvo-secrets"

# 4. Launch VS Code (must be authenticated with Azure CLI)
code /path/to/HVOv9
```

**Benefits:**
- Centralized secret management
- Audit logging
- Access policies and RBAC
- Automatic rotation support
- Works across multiple environments

### Option 2: macOS Keychain (Local Mac Development)

**Setup:**
```bash
# Store secrets in macOS Keychain
security add-generic-password -s "HVO" -a "ssh-private-key-b64" -w "$(base64 -i ~/.ssh/your_key)"
security add-generic-password -s "HVO" -a "ssh-public-key-b64" -w "$(base64 -i ~/.ssh/your_key.pub)"

# Launch VS Code - secrets will be automatically fetched
code /path/to/HVOv9
```

**Benefits:**
- Integrated with macOS security
- No additional cloud services required
- Encrypted storage
- Touch ID/Face ID protection available

### Option 3: AWS Secrets Manager

**Setup:**
```bash
# 1. Store secrets in AWS Secrets Manager
aws secretsmanager create-secret --name "hvo/ssh-private-key" --secret-string "$(base64 -i ~/.ssh/your_key)"
aws secretsmanager create-secret --name "hvo/ssh-public-key" --secret-string "$(base64 -i ~/.ssh/your_key.pub)"

# 2. Set environment variable
export HVO_AWS_SECRET_NAME="hvo"

# 3. Launch VS Code (must be authenticated with AWS CLI)
code /path/to/HVOv9
```

### Option 4: Local Environment File (Fallback)

If external secret stores are not available, the system falls back to the local environment file:

```bash
# Copy and edit the local environment file
cp .devcontainer/devcontainer.local.env.example .devcontainer/devcontainer.local.env
code .devcontainer/devcontainer.local.env

# The dev container will use these values if external stores are unavailable
```

## How It Works

1. **During dev container startup**, the `advanced-secret-manager.sh` script runs
2. **It detects the environment**:
   - GitHub Codespaces: Uses repository secrets automatically
   - Local with Azure: Tries Azure Key Vault
   - Local with AWS: Tries AWS Secrets Manager  
   - Local Mac: Tries macOS Keychain
   - Fallback: Uses `devcontainer.local.env` file

3. **Secrets are injected** as environment variables before the container fully starts
4. **Applications can access secrets** normally via environment variables

## Authentication Requirements

### Azure Key Vault
- Must be authenticated with Azure CLI: `az login`
- Requires `Key Vault Secrets User` role or similar
- Set `HVO_AZURE_KEYVAULT_NAME` environment variable

### AWS Secrets Manager
- Must be authenticated with AWS CLI: `aws configure` or AWS SSO
- Requires `secretsmanager:GetSecretValue` permission
- Set `HVO_AWS_SECRET_NAME` environment variable

### macOS Keychain
- No additional authentication (uses your Mac login)
- Secrets stored in login keychain
- May prompt for keychain access on first use

## Security Best Practices

1. **Use managed identities** where possible (Azure Managed Identity, AWS IAM roles)
2. **Rotate secrets regularly** (Key Vault and Secrets Manager support automatic rotation)
3. **Use least-privilege access** (grant minimal permissions needed)
4. **Enable audit logging** (track secret access)
5. **Use separate secrets per environment** (dev/staging/prod)

## Environment Variables

The following environment variables control secret fetching:

- `HVO_AZURE_KEYVAULT_NAME`: Azure Key Vault name
- `HVO_AWS_SECRET_NAME`: AWS Secrets Manager secret name prefix
- `CODESPACES`: Automatically set in GitHub Codespaces

## Troubleshooting

### Secrets Not Loading
1. Check authentication: `az account show` or `aws sts get-caller-identity`
2. Verify permissions to access the secret store
3. Check environment variables are set correctly
4. Review container logs during startup

### Fallback to Local File
If external secret stores fail, the system uses `devcontainer.local.env`. Check:
1. File exists and has correct format
2. Values are properly base64 encoded
3. No syntax errors in the file

### Azure Key Vault Issues
```bash
# Test access
az keyvault secret show --vault-name "your-vault" --name "hvo-ssh-private-key"

# Check permissions
az keyvault show --name "your-vault" --query "properties.accessPolicies"
```

### AWS Secrets Manager Issues
```bash
# Test access
aws secretsmanager get-secret-value --secret-id "hvo/ssh-private-key"

# Check permissions
aws iam simulate-principal-policy --policy-source-arn $(aws sts get-caller-identity --query Arn --output text) --action-names secretsmanager:GetSecretValue --resource-arns "*"
```

## Migration Path

1. **Start with local environment file** (current setup)
2. **Choose a secret store** based on your cloud provider preference
3. **Set up authentication** with the chosen service
4. **Store secrets** in the external service
5. **Set environment variables** to enable the integration
6. **Test** by rebuilding the dev container
7. **Remove local secrets** once external integration is confirmed working

This approach provides enterprise-grade secret management while maintaining backward compatibility with the simple local file approach.