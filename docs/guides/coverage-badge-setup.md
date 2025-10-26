# Coverage Badge Setup Guide

This guide explains how to set up a dynamic coverage badge for the HVOv9 repository using GitHub Actions and Shields.io.

## Quick Overview

The coverage badge in the README displays live test coverage percentage from the main branch. It uses:
1. GitHub Actions to compute coverage from Cobertura XML
2. A GitHub Gist to store the coverage JSON
3. Shields.io endpoint badge to display the percentage

## Current Badge

```markdown
![Coverage](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/RoySalisbury/coverage-badge-gist-id/raw/coverage-badge.json)
```

**Note**: Replace `coverage-badge-gist-id` with the actual Gist ID after setup.

## Setup Steps

### 1. Create a GitHub Gist for Coverage Data

1. Go to https://gist.github.com
2. Create a new **secret** Gist with:
   - Filename: `coverage-badge.json`
   - Content:
     ```json
     {
       "schemaVersion": 1,
       "label": "coverage",
       "message": "0%",
       "color": "red"
     }
     ```
3. Save the Gist and note the Gist ID from the URL (e.g., `abc123def456...`)

### 2. Create a GitHub Personal Access Token

1. Go to https://github.com/settings/tokens
2. Generate a new classic token with:
   - **Name**: "HVOv9 Coverage Badge"
   - **Scopes**: `gist` (only)
   - **Expiration**: Set as needed
3. Copy the token (you won't see it again)

### 3. Add Repository Secret

1. Go to repository Settings → Secrets and variables → Actions
2. Add a new repository secret:
   - **Name**: `GIST_TOKEN`
   - **Value**: Paste the token from step 2

### 4. Update the Workflow

Add a new job to `.github/workflows/dotnet.yml` (or create a separate coverage workflow):

```yaml
  coverage-badge:
    needs: [test-unit]
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main' && github.event_name == 'push'
    
    steps:
      - name: Download all coverage artifacts
        uses: actions/download-artifact@v4
        with:
          pattern: test-results-unit-*
          path: coverage-reports
          merge-multiple: true

      - name: Compute coverage percentage
        id: coverage
        run: |
          # Install coverage report tools
          dotnet tool install -g dotnet-reportgenerator-globaltool
          
          # Generate summary from all Cobertura files
          reportgenerator \
            -reports:"coverage-reports/**/coverage.cobertura.xml" \
            -targetdir:coverage-summary \
            -reporttypes:Badges
          
          # Extract coverage percentage from generated badge
          COVERAGE=$(grep -oP 'coverage-\K[0-9.]+' coverage-summary/badge_linecoverage.svg | head -1)
          echo "percentage=$COVERAGE" >> $GITHUB_OUTPUT
          
          # Determine badge color
          if (( $(echo "$COVERAGE >= 80" | bc -l) )); then
            COLOR="green"
          elif (( $(echo "$COVERAGE >= 60" | bc -l) )); then
            COLOR="yellow"
          else
            COLOR="red"
          fi
          echo "color=$COLOR" >> $GITHUB_OUTPUT

      - name: Update Gist with coverage badge
        env:
          GIST_TOKEN: ${{ secrets.GIST_TOKEN }}
          GIST_ID: YOUR_GIST_ID_HERE  # Replace with actual Gist ID
        run: |
          cat > coverage-badge.json <<EOF
          {
            "schemaVersion": 1,
            "label": "coverage",
            "message": "${{ steps.coverage.outputs.percentage }}%",
            "color": "${{ steps.coverage.outputs.color }}"
          }
          EOF
          
          curl -X PATCH \
            -H "Authorization: token $GIST_TOKEN" \
            -H "Content-Type: application/json" \
            -d "{\"files\":{\"coverage-badge.json\":{\"content\":\"$(cat coverage-badge.json | jq -c .)\"}}}" \
            "https://api.github.com/gists/$GIST_ID"
```

### 5. Update README Badge URL

Replace the placeholder in `README.md`:

```markdown
![Coverage](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/RoySalisbury/YOUR_GIST_ID_HERE/raw/coverage-badge.json)
```

## Alternative: Use codecov.io or coveralls.io

For more advanced coverage reporting with history, trends, and PR comments:

### Codecov Setup

1. Sign up at https://codecov.io with your GitHub account
2. Enable the HVOv9 repository
3. Add to workflow (after test jobs):

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

4. Add badge to README:

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

## Badge Color Thresholds

Default color mapping:
- **Green**: ≥ 80% coverage
- **Yellow**: 60-79% coverage
- **Orange**: 40-59% coverage
- **Red**: < 40% coverage

Adjust thresholds in the workflow script as needed.

## Testing the Setup

1. Make a small change to a test file
2. Commit and push to main
3. Wait for the workflow to complete
4. Check the Gist to see the updated JSON
5. Verify the badge in README reflects the new coverage

## Troubleshooting

### Badge shows "invalid"
- Check that the Gist URL is correct and publicly accessible
- Verify the JSON format in the Gist matches the schema

### Badge doesn't update
- Ensure `GIST_TOKEN` secret is set correctly
- Check workflow logs for API errors
- Verify the Gist ID in the workflow matches your Gist

### Coverage percentage seems wrong
- Verify all coverage artifacts are being downloaded
- Check that reportgenerator is finding all Cobertura files
- Review the `Include`/`Exclude` patterns in `coverage.runsettings`

## Maintenance

- Regenerate the `GIST_TOKEN` before expiration
- Review coverage trends after major refactors
- Adjust color thresholds as coverage improves
- Consider per-domain coverage badges for large repos

## References

- [Shields.io Endpoint Badges](https://shields.io/endpoint)
- [ReportGenerator](https://github.com/danielpalme/ReportGenerator)
- [Codecov Documentation](https://docs.codecov.com)
- [Coveralls Documentation](https://docs.coveralls.io)
