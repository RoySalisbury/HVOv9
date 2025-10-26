#!/usr/bin/env bash
set -euo pipefail

# Clear SkyMonitor S3/MinIO objects for the configured bucket/prefixes.
#
# Defaults are aligned with HVO.SkyMonitorV5.RPi appsettings Processed/Raw S3 configs:
#   Endpoint: http://192.168.2.104:9000
#   Bucket:   hvo-skymonitor
#   Region:   us-west-2
#   Prefixes: processed, raw
#
# Credentials are read from environment variables:
#   HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY (required)
#   HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY (required)
# Optional env overrides:
#   S3_ENDPOINT, S3_BUCKET, S3_REGION, S3_PREFIXES (comma-separated)
#
# Safety features:
# - Dry run by default (use --confirm to actually delete)
# - Explicit output of what will be deleted
# - Ability to target one or both prefixes
#
# Dependencies:
# - MinIO CLI (mc) preferred, or AWS CLI (aws). If neither is installed, falls back to dockerized aws-cli.
#
# Usage examples:
#   bash scripts/clear-skymonitor-s3.sh                       # dry run, both prefixes
#   S3_PREFIXES=processed bash scripts/clear-skymonitor-s3.sh # dry run, only processed
#   bash scripts/clear-skymonitor-s3.sh --confirm             # actually delete
#   bash scripts/clear-skymonitor-s3.sh --confirm --endpoint http://minio:9000 --bucket mybucket
#

ENDPOINT=${S3_ENDPOINT:-"http://192.168.2.104:9000"}
BUCKET=${S3_BUCKET:-"hvo-skymonitor"}
REGION=${S3_REGION:-"us-west-2"}
PREFIXES_CSV=${S3_PREFIXES:-"processed,raw"}
CONFIRM=false

print_usage() {
  cat <<EOF
Clear SkyMonitor S3/MinIO objects for bucket '$BUCKET' at '$ENDPOINT'.

Options:
  --confirm                 Actually delete (default is dry run)
  --endpoint <url>          Override endpoint (default: $ENDPOINT)
  --bucket <name>           Override bucket (default: $BUCKET)
  --region <aws-region>     Override region (default: $REGION)
  --prefixes <p1,p2>        Comma-separated prefixes (default: $PREFIXES_CSV)
  -h, --help                Show this help

Environment:
  HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY (required)
  HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY (required)
  S3_ENDPOINT, S3_BUCKET, S3_REGION, S3_PREFIXES (optional overrides)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --confirm) CONFIRM=true; shift ;;
    --endpoint) ENDPOINT="$2"; shift 2 ;;
    --bucket) BUCKET="$2"; shift 2 ;;
    --region) REGION="$2"; shift 2 ;;
    --prefixes) PREFIXES_CSV="$2"; shift 2 ;;
    -h|--help) print_usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; print_usage; exit 2 ;;
  esac
done

# Map HVO secret env vars to AWS CLI expected names
export AWS_ACCESS_KEY_ID="${HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY:-}"
export AWS_SECRET_ACCESS_KEY="${HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY:-}"

if [[ -z "$AWS_ACCESS_KEY_ID" || -z "$AWS_SECRET_ACCESS_KEY" ]]; then
  echo "ERROR: HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY and HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY must be set in the environment." >&2
  exit 1
fi

# Resolve prefixes array
IFS=',' read -r -a PREFIXES <<< "$PREFIXES_CSV"

# Detect which CLI tool to use
have_mc() { command -v mc >/dev/null 2>&1; }
have_aws() { command -v aws >/dev/null 2>&1; }
have_docker() { command -v docker >/dev/null 2>&1; }

USE_MC=false
USE_AWS=false
MC_ALIAS="hvominio"

if have_mc; then
  USE_MC=true
  # Configure MinIO alias if not already present
  if ! mc alias list "$MC_ALIAS" >/dev/null 2>&1; then
    echo "Configuring MinIO alias '$MC_ALIAS' -> $ENDPOINT"
    mc alias set "$MC_ALIAS" "$ENDPOINT" "$AWS_ACCESS_KEY_ID" "$AWS_SECRET_ACCESS_KEY" --api S3v4 >/dev/null
  fi
elif have_aws; then
  USE_AWS=true
elif have_docker; then
  USE_AWS=true
  echo "Using dockerized AWS CLI"
else
  echo "ERROR: No suitable CLI found. Install 'mc' (MinIO client), 'aws' (AWS CLI), or 'docker'." >&2
  exit 1
fi

run_s3_list() {
  local prefix="$1"
  if [[ "$USE_MC" == true ]]; then
    mc ls --recursive "$MC_ALIAS/$BUCKET/$prefix" 2>/dev/null | head -n 10 || true
  else
    run_aws --endpoint-url "$ENDPOINT" --no-verify-ssl s3 ls "s3://$BUCKET/$prefix" --recursive 2>/dev/null | head -n 10 || true
  fi
}

run_s3_remove() {
  local prefix="$1"
  shift
  local extra_args=("$@")
  
  if [[ "$USE_MC" == true ]]; then
    mc rm --recursive --force "${extra_args[@]}" "$MC_ALIAS/$BUCKET/$prefix"
  else
    run_aws --endpoint-url "$ENDPOINT" --no-verify-ssl s3 rm "s3://$BUCKET/$prefix" --recursive "${extra_args[@]}"
  fi
}

run_aws() {
  if have_aws; then
    AWS_DEFAULT_REGION="$REGION" aws "$@"
  else
    # Dockerized aws-cli
    docker run --rm \
      -e AWS_ACCESS_KEY_ID -e AWS_SECRET_ACCESS_KEY -e AWS_DEFAULT_REGION="$REGION" \
      amazon/aws-cli:2 \
      aws "$@"
  fi
}

DRYRUN_ARGS=()
if [[ "$CONFIRM" != true ]]; then
  if [[ "$USE_MC" == true ]]; then
    DRYRUN_ARGS+=(--dry-run)
  else
    DRYRUN_ARGS+=(--dryrun)
  fi
  echo "[DRY RUN] No objects will be deleted. Use --confirm to actually delete." >&2
fi

echo "Endpoint : $ENDPOINT"
echo "Bucket   : $BUCKET"
echo "Region   : $REGION"
echo "CLI Tool : $(if [[ "$USE_MC" == true ]]; then echo "MinIO mc"; else echo "AWS CLI"; fi)"
printf "Prefixes : %s\n" "${PREFIXES[*]}"

# Iterate prefixes; show counts then delete
for prefix in "${PREFIXES[@]}"; do
  echo "---"
  echo "Listing objects under s3://$BUCKET/$prefix (first 10)"
  run_s3_list "$prefix"

  echo "Deleting objects under s3://$BUCKET/$prefix ${CONFIRM:+(CONFIRMED)}" 
  run_s3_remove "$prefix" "${DRYRUN_ARGS[@]}"

done

echo "Done."