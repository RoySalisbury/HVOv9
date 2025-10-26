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
# - aws CLI. If not installed, this script will try a dockerized aws-cli.
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

have_aws() { command -v aws >/dev/null 2>&1; }

run_aws() {
  if have_aws; then
    AWS_DEFAULT_REGION="$REGION" aws "$@"
  else
    # Try dockerized aws-cli
    if ! command -v docker >/dev/null 2>&1; then
      echo "ERROR: aws CLI not found and docker is unavailable to run aws-cli." >&2
      exit 1
    fi
    docker run --rm \
      -e AWS_ACCESS_KEY_ID -e AWS_SECRET_ACCESS_KEY -e AWS_DEFAULT_REGION="$REGION" \
      amazon/aws-cli:2 \
      aws "$@"
  fi
}

DRYRUN_ARGS=()
if [[ "$CONFIRM" != true ]]; then
  DRYRUN_ARGS+=(--dryrun)
  echo "[DRY RUN] No objects will be deleted. Use --confirm to actually delete." >&2
fi

echo "Endpoint : $ENDPOINT"
echo "Bucket   : $BUCKET"
echo "Region   : $REGION"
printf "Prefixes : %s\n" "${PREFIXES[*]}"

# Iterate prefixes; show counts then delete
for prefix in "${PREFIXES[@]}"; do
  echo "---"
  echo "Listing objects under s3://$BUCKET/$prefix"
  # Using s3api with pagination could be added if needed; for now rely on s3 rm --recursive to handle all.
  # Show a quick head listing (first 10 objects)
  run_aws --endpoint-url "$ENDPOINT" --no-verify-ssl s3 ls "s3://$BUCKET/$prefix" --recursive | head -n 10 || true

  echo "Deleting objects under s3://$BUCKET/$prefix ${CONFIRM:+(CONFIRMED)}" 
  run_aws --endpoint-url "$ENDPOINT" --no-verify-ssl s3 rm "s3://$BUCKET/$prefix" --recursive "${DRYRUN_ARGS[@]}"

done

echo "Done."