# Coverage Badge Setup Guide

This guide explains how to set up a dynamic coverage badge for the HVOv9 repository using GitHub Actions and Shields.io.

## Quick Overview

The coverage badge workflow is **already implemented** in `.github/workflows/dotnet.yml`. You just need to configure the secrets and variables to enable badge updates.

The coverage badge displays live test coverage percentage from the main branch using:
1. **GitHub Actions** (already configured) - Computes coverage from Cobertura XML files
2. **GitHub Gist** (you need to create) - Stores the coverage badge JSON
3. **Shields.io endpoint badge** - Displays the percentage in README

## Current Implementation Status

✅ **Workflow configured** - The `coverage-badge` job is ready in `.github/workflows/dotnet.yml`  
✅ **Coverage collection enabled** - All test jobs collect coverage via `src/coverage.runsettings`  
⏳ **Waiting for setup** - Requires Gist creation and secrets configuration (see below)

## Setup Steps (One-Time Configuration)

### 1. Create a GitHub Gist for Coverage Data

1. Go to https://gist.github.com
2. Create a new **public** Gist with:
   - **Description**: "HVOv9 Coverage Badge"
   - **Filename**: `coverage-badge.json`
   - **Content** (placeholder, will be auto-updated):
     ```json
     {
       "schemaVersion": 1,
       "label": "coverage",
       "message": "pending",
       "color": "lightgrey"
     }
     ```
3. Click "Create public gist"
4. Copy the Gist ID from the URL (e.g., if URL is `https://gist.github.com/RoySalisbury/abc123def456`, the ID is `abc123def456`)

### 2. Create a GitHub Personal Access Token

1. Go to https://github.com/settings/tokens
2. Click "Generate new token" → "Generate new token (classic)"
3. Configure the token:
   - **Note**: "HVOv9 Coverage Badge"
   - **Expiration**: 90 days (or longer, you'll need to regenerate periodically)
   - **Scopes**: Check **only** `gist` (read/write access to gists)
4. Click "Generate token"
5. **Copy the token immediately** (you won't be able to see it again)

### 3. Configure Repository Secrets and Variables

1. Go to https://github.com/RoySalisbury/HVOv9/settings/secrets/actions

2. **Add Repository Secret** (for the token):
   - Click "New repository secret"
   - **Name**: `GIST_TOKEN`
   - **Value**: Paste the Personal Access Token from step 2
   - Click "Add secret"

3. **Add Repository Variable** (for the Gist ID):
   - Click the "Variables" tab
   - Click "New repository variable"
   - **Name**: `COVERAGE_GIST_ID`
   - **Value**: Paste the Gist ID from step 1
   - Click "Add variable"

### 4. Update README Badge URL

1. Edit `/README.md`
2. Find the coverage badge section (around line 24)
3. Replace `YOUR_GIST_ID_HERE` with your actual Gist ID:
   ```markdown
   ![Coverage](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/RoySalisbury/YOUR_GIST_ID_HERE/raw/coverage-badge.json)
   ```
4. Uncomment the badge line (remove the `<!-- -->` comments)
5. Commit the change

### 5. Trigger the Workflow

The coverage badge will update automatically on the next push to `main`. To test immediately:

```bash
git commit --allow-empty -m "Trigger coverage badge update"
git push origin main
```

The workflow will:
1. Run unit tests with coverage collection
2. Download all coverage artifacts
3. Merge coverage data and calculate overall percentage
4. Update your Gist with the new badge JSON
5. The badge in README will reflect the update within minutes

## How It Works

The `coverage-badge` job in `.github/workflows/dotnet.yml` automatically:

1. **Runs after unit tests complete** - Depends on the `test-unit` job
2. **Downloads coverage artifacts** - Merges all Cobertura XML files from test matrix
3. **Calculates coverage percentage** - Uses ReportGenerator to analyze coverage
4. **Determines badge color**:
   - 🟢 **Bright Green**: ≥ 80% coverage
   - 🟡 **Yellow**: 60-79% coverage  
   - 🟠 **Orange**: 40-59% coverage
   - 🔴 **Red**: < 40% coverage
5. **Updates GitHub Gist** - Patches the Gist with new badge JSON via GitHub API
6. **Badge auto-refreshes** - Shields.io fetches the updated JSON and displays the new badge

## Verifying the Setup

### Check Workflow Execution

After pushing to `main`, verify the coverage badge job runs:

1. Go to https://github.com/RoySalisbury/HVOv9/actions
2. Click on the latest ".NET Build & Test" workflow run
3. Confirm the `coverage-badge` job appears and completes successfully
4. Check the job logs for "Coverage percentage: X%" message

### Check Gist Update

1. Go to your Gist URL: `https://gist.github.com/RoySalisbury/YOUR_GIST_ID`
2. Open the `coverage-badge.json` file
3. Verify it contains updated coverage data:
   ```json
   {
     "schemaVersion": 1,
     "label": "coverage",
     "message": "75.3%",
     "color": "yellow"
   }
   ```

### Check Badge Display

1. View the README: https://github.com/RoySalisbury/HVOv9
2. The coverage badge should display with the correct percentage and color
3. Click the badge - it should link to the workflow runs page

## Alternative: Use codecov.io or coveralls.io

For more advanced coverage reporting with history, trends, and PR comments, consider using a dedicated coverage service:

### Codecov Setup

1. Sign up at https://codecov.io with your GitHub account
2. Enable the HVOv9 repository
3. Get your Codecov token and add as `CODECOV_TOKEN` repository secret
4. Add to workflow (after test jobs):

```yaml
  upload-coverage:
    needs: [test-unit, test-integration]
    runs-on: ubuntu-latest
    
    steps:
      - name: Download coverage artifacts
        uses: actions/download-artifact@v4
        with:
          pattern: test-results-*
          path: coverage-reports
          merge-multiple: true

      - name: Upload to Codecov
        uses: codecov/codecov-action@v4
        with:
          files: coverage-reports/**/coverage.cobertura.xml
          token: ${{ secrets.CODECOV_TOKEN }}
          flags: unittests
          name: codecov-umbrella
```

5. Add badge to README:

```markdown
[![codecov](https://codecov.io/gh/RoySalisbury/HVOv9/branch/main/graph/badge.svg)](https://codecov.io/gh/RoySalisbury/HVOv9)
```

### Coveralls Setup

1. Sign up at https://coveralls.io
2. Enable HVOv9 repository  
3. Add to workflow:

```yaml
  - name: Coveralls
    uses: coverallsapp/github-action@v2
    with:
      github-token: ${{ secrets.GITHUB_TOKEN }}
      path-to-lcov: coverage-reports/**/coverage.cobertura.xml
      format: cobertura
```

## Troubleshooting

### Coverage badge job doesn't run

**Symptom**: The `coverage-badge` job is skipped in workflow runs

**Solutions**:
- Verify the push is to the `main` branch (job only runs on main)
- Check that it's a `push` event (not a PR or other trigger)
- Ensure the `test-unit` job completed successfully (dependency requirement)

### Badge shows "invalid" or doesn't load

**Symptom**: Badge displays "invalid" or fails to load in README

**Solutions**:
- Verify the Gist is **public** (not secret) - Shields.io can't access secret gists
- Check the Gist URL in README matches your actual Gist ID
- Confirm `coverage-badge.json` file exists in the Gist
- Wait a few minutes for Shields.io cache to refresh
- Try accessing the Gist JSON directly: `https://gist.githubusercontent.com/RoySalisbury/YOUR_GIST_ID/raw/coverage-badge.json`

### Badge doesn't update after workflow runs

**Symptom**: Workflow completes but badge still shows old percentage

**Solutions**:
- Check the workflow logs for the "Update Gist with coverage badge" step
- Verify `GIST_TOKEN` secret is set correctly in repository settings
- Confirm `COVERAGE_GIST_ID` variable matches your actual Gist ID
- Look for API error messages in the workflow logs
- Ensure the token has `gist` scope and hasn't expired
- Check if the Gist shows "PLACEHOLDER_GIST_ID" message in logs (means variable not set)

### Coverage percentage seems incorrect

**Symptom**: Badge shows 0% or unexpectedly low/high coverage

**Solutions**:
- Download the `coverage-summary` artifact from the workflow run
- Review `Summary.txt` for detailed coverage breakdown
- Check that all test projects are included in the test matrix
- Verify coverage collection is working: look for `coverage.cobertura.xml` files in test artifacts
- Review the `Include`/`Exclude` patterns in `src/coverage.runsettings`
- Ensure test projects reference `coverlet.collector` package

### API rate limit or authentication errors

**Symptom**: Workflow fails with 401/403/429 HTTP errors

**Solutions**:
- **401 Unauthorized**: Token is invalid or expired - regenerate `GIST_TOKEN`
- **403 Forbidden**: Token lacks `gist` scope - create new token with proper permissions
- **429 Rate Limit**: Too many API calls - GitHub API limits are high, but wait an hour if hit
- Check the token is properly set as `GIST_TOKEN` (not `GITHUB_TOKEN` or other name)

### No coverage files found

**Symptom**: Workflow reports "No coverage files found"

**Solutions**:
- Verify test jobs completed and uploaded artifacts
- Check test job logs confirm coverage collection with `--settings coverage.runsettings`
- Ensure `coverlet.collector` package is referenced in test projects
- Look for "Data collection" messages in test output
- Verify artifact upload includes `**/coverage.cobertura.xml` pattern

## Maintenance

### Token Expiration
- Personal access tokens expire based on the expiration period you set
- GitHub will email you before token expires
- Regenerate token before expiration and update `GIST_TOKEN` secret
- Same Gist can be reused with new token

### Coverage Trend Monitoring
- Download `coverage-summary` artifacts periodically to track trends
- Review coverage after major refactors or new feature development
- Consider setting up Codecov/Coveralls for historical tracking

### Adjusting Thresholds
Current color thresholds in the workflow:
```yaml
if (( $(echo "$COVERAGE >= 80" | bc -l) )); then
  COLOR="brightgreen"   # ≥ 80%
elif (( $(echo "$COVERAGE >= 60" | bc -l) )); then
  COLOR="yellow"        # 60-79%
elif (( $(echo "$COVERAGE >= 40" | bc -l) )); then
  COLOR="orange"        # 40-59%
else
  COLOR="red"           # < 40%
fi
```

To adjust thresholds, edit `.github/workflows/dotnet.yml` and modify the comparison values.

## References

- [Shields.io Endpoint Badges](https://shields.io/endpoint)
- [ReportGenerator Documentation](https://github.com/danielpalme/ReportGenerator)
- [Codecov Documentation](https://docs.codecov.com)
- [Coveralls Documentation](https://docs.coveralls.io)
- [GitHub Gists Documentation](https://docs.github.com/en/github/writing-on-github/editing-and-sharing-content-with-gists)
- [GitHub Personal Access Tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token)
