#!/usr/bin/env bash

set -euo pipefail

KEEP=10
REPO=""
BRANCH=""
STATUS="completed"
DRY_RUN=0

usage() {
  cat <<'EOF'
Usage: cleanup-workflow-runs.sh [options]

List GitHub Actions workflow runs and delete all but the latest runs.

Options:
  -r, --repo <owner/repo>   Target repository (defaults to current directory's repo)
  -k, --keep <count>        Number of latest runs to keep (default: 10)
      --branch <name>       Filter runs by branch
      --status <state>      Filter runs by status (default: completed)
  -n, --dry-run             Show which runs would be deleted without deleting
  -h, --help                Show this help message

Requires GitHub CLI (gh) with authenticated access.
EOF
}

log() {
  printf '%s\n' "$1" >&2
}

require_gh() {
  if ! command -v gh >/dev/null 2>&1; then
    log "GitHub CLI (gh) is required but not installed."
    exit 1
  fi

  if ! gh auth status >/dev/null 2>&1; then
    log "GitHub CLI is not authenticated. Run 'gh auth login' first."
    exit 1
  fi
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      -r|--repo)
        REPO="$2"
        shift 2
        ;;
      -k|--keep)
        KEEP="$2"
        shift 2
        ;;
      --branch)
        BRANCH="$2"
        shift 2
        ;;
      --status)
        STATUS="$2"
        shift 2
        ;;
      -n|--dry-run)
        DRY_RUN=1
        shift
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        log "Unknown option: $1"
        usage
        exit 1
        ;;
    esac
  done

  if ! [[ "$KEEP" =~ ^[0-9]+$ ]] || (( KEEP < 0 )); then
    log "--keep must be a non-negative integer"
    exit 1
  fi
}

resolve_repo() {
  if [[ -n "$REPO" ]]; then
    return
  fi

  if ! REPO=$(gh repo view --json nameWithOwner --jq '.nameWithOwner' 2>/dev/null); then
    log "Unable to determine repository. Use --repo to specify one explicitly."
    exit 1
  fi
}

fetch_runs() {
  local query="per_page=100"

  if [[ -n "$STATUS" ]]; then
    query+="&status=$STATUS"
  fi

  if [[ -n "$BRANCH" ]]; then
    query+="&branch=$BRANCH"
  fi

  mapfile -t RUN_LINES < <(gh api --paginate "/repos/$REPO/actions/runs?$query" \
    --jq '.workflow_runs[] | [.id, .name, .display_title, .status, .conclusion, .created_at, .html_url] | @tsv')
}

print_runs() {
  local -n _lines_ref=$1
  local label=$2
  local limit=$3
  local start_index=$4

  if (( ${#_lines_ref[@]} == 0 )); then
    return
  fi

  printf '\n%s\n' "$label"
  printf '%s\n' '--------------------------------------------------------------------------------'

  local count=0
  for ((i=start_index; i < ${#_lines_ref[@]} && count < limit; i++)); do
    if ! IFS=$'\t' read -r run_id name title status conclusion created_at html_url <<<"${_lines_ref[i]}"; then
      continue
    fi
    local display_title=${title:-$name}
    local display_status=${status:-"-"}
    local display_conclusion=${conclusion:-"-"}

    printf '#%d Run ID: %s\n' "$((i + 1))" "$run_id"
    printf '   Workflow: %s\n' "$display_title"
    printf '   Status: %s | Conclusion: %s\n' "$display_status" "$display_conclusion"
    printf '   Created: %s\n' "$created_at"
    printf '   URL: %s\n' "$html_url"
  printf '%s\n' '--------------------------------------------------------------------------------'
  ((count += 1))
  done
}

delete_runs() {
  local -n _lines_ref=$1
  local start_index=$2

  for ((i=start_index; i < ${#_lines_ref[@]}; i++)); do
    if ! IFS=$'\t' read -r run_id name title status conclusion created_at html_url <<<"${_lines_ref[i]}"; then
      continue
    fi
    if (( DRY_RUN )); then
      printf 'DRY RUN: Would delete run %s (%s) from %s\n' "$run_id" "${title:-$name}" "$created_at"
    else
      printf 'Deleting run %s (%s) from %s... ' "$run_id" "${title:-$name}" "$created_at"
      gh api --silent --method DELETE "/repos/$REPO/actions/runs/$run_id"
      printf 'done.\n'
    fi
  done
}

main() {
  parse_args "$@"
  require_gh
  resolve_repo

  log "Target repository: $REPO"
  log "Fetching workflow runs (status=$STATUS${BRANCH:+, branch=$BRANCH})..."

  fetch_runs
  local total_runs=${#RUN_LINES[@]}

  if (( total_runs == 0 )); then
    log "No workflow runs found."
    exit 0
  fi

  print_runs RUN_LINES "Latest runs" "$KEEP" 0

  if (( total_runs <= KEEP )); then
    printf '\nTotal runs: %d. Nothing to delete.\n' "$total_runs"
    exit 0
  fi

  local delete_count=$(( total_runs - KEEP ))
  printf '\nTotal runs: %d. Will delete %d run(s).\n' "$total_runs" "$delete_count"
  print_runs RUN_LINES "Runs pending deletion" "$delete_count" "$KEEP"

  printf '\nStarting deletion of %d run(s)...\n' "$delete_count"
  delete_runs RUN_LINES "$KEEP"
  printf '\nCleanup complete. Kept %d latest run(s).\n' "$KEEP"
}

main "$@"
