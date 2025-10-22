# Environment Setup for HVOv9 Dev Container

## Overview

The HVOv9 dev container uses a robust environment variable system that works reliably in both local development and GitHub Codespaces without requiring pre-existing files.

## How It Works

The dev container configuration uses `containerEnv` with `${localEnv:VARIABLE_NAME:default_value}` syntax to:

1. **Local Development**: Read from `.devcontainer/devcontainer.local.env` if it exists
2. **GitHub Codespaces**: Use Codespace secrets 
3. **Fallback**: Use safe development defaults when neither is available

## Local Development Setup

### Option 1: Environment File (Recommended)
1. Copy the example file:
   ```bash
   cp .devcontainer/devcontainer.local.env.example .devcontainer/devcontainer.local.env
   ```
2. Edit `devcontainer.local.env` with your actual values
3. The file is gitignored and will be automatically loaded

### Option 2: Host Environment Variables
Export the variables in your host shell before starting the dev container:
```bash
export HVO_SECRET__WEBSITEV9__DB_CONNECTION="your-connection-string"
export HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY="your-api-key"
# ... etc
```

## GitHub Codespaces Setup

### Automatic Setup (Recommended)
Run the setup script to get guidance on configuring Codespace secrets:
```bash
./scripts/setup-codespaces-secrets.sh
```

### Manual Setup
Configure repository secrets in GitHub:
1. Go to your repository → Settings → Secrets and variables → Codespaces
2. Add repository secrets for each environment variable
3. Use the exact names from the table below

## Post-Create Script Optimizations

The `scripts/post-create.sh` script has been optimized to avoid redundant package installations:

### What's Included in Base Image (mcr.microsoft.com/devcontainers/dotnet:9.0)
- .NET 9.0 SDK and runtime
- Git, curl, unzip, zip, tar, gzip
- Build tools (make, build-essential, pkg-config)
- Common utilities (jq, nano, ca-certificates)
- Basic networking tools (iproute2, net-tools)

### What We Install (HVO-Specific)
- **Hardware/IoT**: `libgpiod-dev`, `i2c-tools` for GPIO development
- **Fonts**: Full font stack for image processing and UI rendering
- **Python Scientific**: `python3-numpy`, `python3-pil` for analysis scripts
- **Dev Container Features**: GitHub CLI, Python tools via official features
- **.NET Tools**: Entity Framework CLI, diagnostics, scripting tools

### What Was Removed
- Redundant base packages already in the container
- Manual GitHub CLI setup (now uses dev container feature)
- Packages that are guaranteed to exist in the base image

## Environment Variables

| Variable | Purpose | Default | Required |
|----------|---------|---------|-----------|
| `HVO_SECRET__WEBSITEV9__DB_CONNECTION` | Database for main site | Local SQL Server | For DB access |
| `HVO_SECRET__WEBSITEPLAYGROUND__DB_CONNECTION` | Database for playground | Local SQL Server | For DB access |
| `HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY` | NINA API integration | Empty | For NINA features |
| `HVO_SECRET__WEBSITEPLAYGROUND__AZDO_PAT` | Azure DevOps integration | Empty | For DevOps features |
| `HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY` | S3 access key | "admin" | For frame export |
| `HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY` | S3 secret key | "change-me-now-32chars" | For frame export |
| `HVO_SECRET__SSH__PRIVATE_KEY_B64` | SSH private key (base64) | Empty | For remote access |
| `HVO_SECRET__SSH__PUBLIC_KEY_B64` | SSH public key (base64) | Empty | For remote access |
| `TS_AUTHKEY` | Tailscale auth key | Empty | For VPN access |
| `TS_HOSTNAME` | Tailscale hostname | "vscode-container" | For VPN access |

## Development Defaults

The dev container provides safe defaults for development:

- **Database**: Uses localhost SQL Server with development credentials
- **API Keys**: Empty (features gracefully degrade)
- **S3 Storage**: Development credentials for local MinIO/testing
- **SSH/Tailscale**: Optional networking features

## Migration from --env-file

The previous `--env-file` approach has been replaced with `containerEnv` for better reliability:

### ✅ **New Approach Benefits:**
- Works in fresh Codespaces without pre-existing files
- Provides sensible development defaults
- Supports both local files and Codespace secrets
- Graceful fallbacks for optional features

### ❌ **Old Issues Resolved:**
- No more "file not found" errors on first clone
- No dependency on post-create scripts for basic functionality
- Better separation between required and optional configuration

## Troubleshooting

### "Connection string not found" errors
1. Check if your environment variables are set correctly
2. Verify the variable names match exactly (case-sensitive)
3. For Codespaces, ensure secrets are set at the repository level

### Variables not loading
1. Rebuild the dev container to pick up new environment configuration
2. Check VS Code dev container logs for any loading errors
3. Verify syntax in `containerEnv` (JSON formatting)

### Local file not loading
1. Ensure `.devcontainer/devcontainer.local.env` exists and has correct syntax
2. Check file permissions (should be readable by your user)
3. Restart VS Code dev container

## Security Notes

- **Never commit** `devcontainer.local.env` (it's gitignored)
- **Use Codespace secrets** for sensitive production values
- **Rotate keys regularly** especially for production environments
- **Use least privilege** for database connections and API keys