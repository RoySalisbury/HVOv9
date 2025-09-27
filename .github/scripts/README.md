# GitHub Maintenance Scripts

## `cleanup-workflow-runs.sh`

Lists the repository's workflow runs via the GitHub CLI and deletes all but the most recent ones.

The script prints a "Latest runs" section (what will be retained) followed by "Runs pending deletion". In dry-run mode, every run scheduled for deletion is prefixed with `DRY RUN:` so you can review the full list before removing anything.

### Requirements

- [GitHub CLI](https://cli.github.com/) (`gh`) installed and authenticated (`gh auth login`)

### Usage

```
./cleanup-workflow-runs.sh [options]
```

Key options:

- `--keep <count>` — number of latest runs to retain (default: 10)
- `--dry-run` — show which runs would be deleted without deleting
- `--repo <owner/repo>` — target a different repository
- `--branch <name>` — restrict runs to a single branch
- `--status <state>` — filter by workflow status (`completed`, `in_progress`, etc.)

Run the script from the repository root:

```bash
.github/scripts/cleanup-workflow-runs.sh --dry-run
```

Tip: adjust `--keep` or add `--branch main` when you want to reduce the amount of output while previewing deletions.

### Codespaces note

In GitHub Codespaces the GitHub CLI is preinstalled and typically authenticated for the current repository. If authentication is required, run `gh auth login --web --scopes actions:write` before executing the script.
