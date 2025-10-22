#!/usr/bin/env bash
# Test script to verify dev container environment loading
# This simulates what happens when VS Code rebuilds the dev container

set -euo pipefail

echo "🧪 Testing HVO Dev Container Environment Loading"
echo "================================================"

# Test 1: Verify local environment file exists
echo "Test 1: Checking local environment file..."
if [[ -f "/workspaces/HVOv9/.devcontainer/devcontainer.local.env" ]]; then
  echo "✅ Local environment file exists"
else
  echo "❌ Local environment file missing"
  exit 1
fi

# Test 2: Verify init script exists and is executable
echo "Test 2: Checking init script..."
if [[ -x "/workspaces/HVOv9/scripts/init-shell-env.sh" ]]; then
  echo "✅ Init script exists and is executable"
else
  echo "❌ Init script missing or not executable"
  exit 1
fi

# Test 3: Verify .bashrc has been updated
echo "Test 3: Checking .bashrc configuration..."
if grep -q "init-shell-env.sh" /home/vscode/.bashrc; then
  echo "✅ .bashrc configured for automatic loading"
else
  echo "❌ .bashrc not configured"
  exit 1
fi

# Test 4: Test manual environment loading
echo "Test 4: Testing manual environment loading..."
# Clear environment first
unset HVO_SECRET__SSH__PRIVATE_KEY_B64 HVO_SECRET__SSH__PUBLIC_KEY_B64 2>/dev/null || true

# Load via init script
if source /workspaces/HVOv9/scripts/init-shell-env.sh; then
  echo "✅ Manual loading successful"
else
  echo "❌ Manual loading failed"
  exit 1
fi

# Test 5: Verify key environment variables are loaded
echo "Test 5: Verifying environment variables..."
if [[ -n "${HVO_SECRET__SSH__PRIVATE_KEY_B64:-}" ]]; then
  echo "✅ SSH private key loaded (${#HVO_SECRET__SSH__PRIVATE_KEY_B64} chars)"
else
  echo "❌ SSH private key not loaded"
  exit 1
fi

if [[ -n "${HVO_SECRET__SSH__PUBLIC_KEY_B64:-}" ]]; then
  echo "✅ SSH public key loaded (${#HVO_SECRET__SSH__PUBLIC_KEY_B64} chars)"
else
  echo "❌ SSH public key not loaded"
  exit 1
fi

# Test 6: Test automatic loading in new shell
echo "Test 6: Testing automatic loading in new shell..."
result=$(bash -c 'source ~/.bashrc &>/dev/null && if [[ -n "${HVO_SECRET__SSH__PRIVATE_KEY_B64:-}" ]]; then echo "SUCCESS"; else echo "FAILED"; fi')
if [[ "$result" == "SUCCESS" ]]; then
  echo "✅ Automatic loading works in new shell"
else
  echo "❌ Automatic loading failed in new shell"
  exit 1
fi

# Test 7: Run comprehensive validation
echo "Test 7: Running comprehensive validation..."
if bash /workspaces/HVOv9/scripts/validate-dev-env.sh >/dev/null 2>&1; then
  echo "✅ Comprehensive validation passed"
else
  echo "❌ Comprehensive validation failed"
  exit 1
fi

echo ""
echo "🎉 All tests passed! Dev container environment loading is working correctly."
echo ""
echo "Summary:"
echo "- ✅ Environment variables load automatically in new shells"
echo "- ✅ SSH keys are available for Docker remote contexts"
echo "- ✅ Database connections are configured"
echo "- ✅ Manual loading works as fallback"
echo "- ✅ Comprehensive validation passes"
echo ""
echo "The dev container will work correctly after rebuild/restart."