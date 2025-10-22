# Dev Container Secrets Setup

This guide explains how to configure secrets for the HVOv9 dev container environment.

## Overview

The HVOv9 dev container supports both local development and GitHub Codespaces. Secrets are handled differently in each environment:

- **Local Development**: Use `devcontainer.local.env` file (git-ignored)
- **GitHub Codespaces**: Use GitHub repository secrets or Codespaces secrets

## Required Secrets

### SSH Keys for Remote Docker Contexts
The following SSH key secrets are required for Docker remote contexts and deployment:

- `HVO_SECRET__SSH__PRIVATE_KEY_B64`: Base64-encoded private SSH key
- `HVO_SECRET__SSH__PUBLIC_KEY_B64`: Base64-encoded public SSH key

### Database Connections
- `HVO_SECRET__WEBSITEV9__DB_CONNECTION`: Connection string for WebSite v9
- `HVO_SECRET__WEBSITEPLAYGROUND__DB_CONNECTION`: Connection string for Playground

### API Keys
- `HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY`: NINA API key for astronomy integration
- `HVO_SECRET__WEBSITEPLAYGROUND__AZDO_PAT`: Azure DevOps Personal Access Token

### Storage Keys
- `HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY`: S3 access key for frame export
- `HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY`: S3 secret key for frame export

## Setup Instructions

### For Local Development (VS Code Dev Containers on Mac/Windows/Linux)

**Quick Setup:**
```bash
# Run the setup script for guided configuration
bash scripts/setup-local-dev-secrets.sh
```

**Manual Setup:**
1. **Create local environment file**:
   ```bash
   cp .devcontainer/devcontainer.local.env.example .devcontainer/devcontainer.local.env
   ```

2. **Edit the file with your actual secrets**:
   ```bash
   # Edit with your editor of choice
   code .devcontainer/devcontainer.local.env
   ```

3. **The file is git-ignored** - your secrets won't be committed

**Important Note:** GitHub repository secrets are NOT automatically available in local dev containers. You must use the `devcontainer.local.env` file for local development.

### For GitHub Codespaces

1. **Repository Secrets** (recommended):
   - Go to your GitHub repository → Settings → Secrets and variables → Codespaces
   - Add each secret with the appropriate name and value
   - These will be automatically available in all Codespaces for this repository

2. **User Secrets** (alternative):
   - Go to your GitHub profile → Settings → Codespaces → Repository secrets
   - Add secrets that will be available across all your Codespaces

**Helper Script:**
```bash
# Get guidance on setting up Codespaces secrets
bash scripts/setup-codespaces-secrets.sh
```

## SSH Key Setup

### Generating SSH Keys for Docker Contexts

If you need to generate SSH keys for remote Docker contexts:

```bash
# Generate SSH key pair
ssh-keygen -t rsa -b 4096 -C "hvo-docker-context" -f ~/.ssh/hvo_docker

# Base64 encode the keys
base64 -w 0 ~/.ssh/hvo_docker > private_key_b64.txt
base64 -w 0 ~/.ssh/hvo_docker.pub > public_key_b64.txt

# Use these base64 strings as the secret values
```

### Setting up SSH Keys on Remote Hosts

1. Copy the **public key** to your remote Docker hosts:
   ```bash
   # Decode and append to authorized_keys on remote host
   echo "HVO_SECRET__SSH__PUBLIC_KEY_B64_VALUE" | base64 -d >> ~/.ssh/authorized_keys
   ```

## Verification

### Check Environment Variables in Dev Container

```bash
# Check if secrets are available (will show empty if not set)
printenv | grep HVO_SECRET

# Verify SSH keys are available (shows first 20 chars if set)
echo "Private key: ${HVO_SECRET__SSH__PRIVATE_KEY_B64:0:20}..."
echo "Public key: ${HVO_SECRET__SSH__PUBLIC_KEY_B64:0:20}..."
```

### Test SSH Connection

```bash
# Test SSH connection to remote Docker host
ssh -i <(echo $HVO_SECRET__SSH__PRIVATE_KEY_B64 | base64 -d) user@remote-host
```

## Troubleshooting

### Secrets Not Available
- **GitHub Codespaces**: Ensure secrets are set at repository or user level
- **Local Development**: Check that `devcontainer.local.env` exists and contains the secrets
- **Both**: Rebuild the dev container after adding secrets

### SSH Keys Not Working
- Verify the base64 encoding is correct (no line breaks)
- Check that the public key is properly installed on remote hosts
- Ensure the private key has correct permissions when decoded

### Database Connection Issues
- Verify connection strings are properly formatted
- Check firewall rules for database access
- Test connection from within the dev container

## Security Notes

- Never commit `devcontainer.local.env` to version control
- Use repository secrets for shared development environments
- Use user secrets for personal Codespaces
- Rotate SSH keys and API keys regularly
- Use least-privilege principles for database connections

## Default Values

The dev container provides safe defaults for development:
- Database connections default to local SQL Server with development credentials
- S3 keys default to MinIO development values
- Missing API keys result in empty values (applications should handle gracefully)
- SSH keys default to empty (Docker contexts will be unavailable)