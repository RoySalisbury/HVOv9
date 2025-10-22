# HVO Dev Container Environment Setup

## Automatic Environment Variable Loading

The HVO dev container automatically loads environment variables from `.devcontainer/devcontainer.local.env` in every new shell session.

### How It Works

1. **Container Creation**: When the dev container is built/rebuilt, `scripts/post-create.sh` runs automatically
2. **Shell Profile Setup**: The post-create script adds an automatic loader to `~/.bashrc`
3. **Automatic Loading**: Every new terminal/shell session automatically sources the environment variables
4. **No Manual Intervention**: Environment variables are available immediately in all new shells

### Files Involved

- **`.devcontainer/devcontainer.local.env`**: Contains all secret environment variables (git-ignored)
- **`scripts/init-shell-env.sh`**: Shell initialization script that loads environment variables
- **`scripts/post-create.sh`**: Container setup script that configures automatic loading
- **`scripts/validate-dev-env.sh`**: Validation script to verify all variables are loaded

### Testing the Setup

```bash
# Validate all environment variables are loaded
bash scripts/validate-dev-env.sh

# Test in a new shell (should have variables automatically)
bash -c 'echo "SSH Key: ${#HVO_SECRET__SSH__PRIVATE_KEY_B64:-0} chars"'

# Manual loading (if needed for troubleshooting)
source scripts/load-local-env.sh
```

### Troubleshooting

If environment variables aren't loading automatically:

1. **Check if .bashrc was updated**: `tail -5 ~/.bashrc` should show the HVO environment loader
2. **Verify local environment file exists**: `ls -la .devcontainer/devcontainer.local.env`
3. **Manual setup**: Run `bash scripts/post-create.sh` to re-run container setup
4. **Test manual loading**: Run `source scripts/load-local-env.sh` to verify file syntax

### Key Benefits

- ✅ **Automatic**: No manual steps required after container rebuild
- ✅ **Persistent**: Environment variables available in every new shell
- ✅ **Efficient**: Only loads if variables aren't already set (no redundant loading)
- ✅ **Robust**: Handles shell quoting and base64 encoding correctly
- ✅ **Validated**: Comprehensive validation ensures all variables are working

The system ensures that SSH keys, database connections, and other secrets are always available for Docker remote contexts, development databases, and other HVO services.