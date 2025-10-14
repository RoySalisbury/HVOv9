#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[user-secrets] dotnet CLI not found. Skipping user secret provisioning." >&2
  exit 0
fi

apply_secret() {
  local project_rel="$1"
  local secret_key="$2"
  local env_var="$3"
  local value

  value="${!env_var-}"
  if [[ -z "${value}" ]]; then
    echo "[user-secrets] Skipping ${project_rel} :: ${secret_key} (env ${env_var} not set)."
    return
  fi

  echo "[user-secrets] Setting ${secret_key} for ${project_rel} from ${env_var}."
  dotnet user-secrets set "${secret_key}" "${value}" --project "${ROOT_DIR}/${project_rel}" >/dev/null
}

# Expected environment variables (configure via GitHub secrets or export locally):
#   HVO_SECRET__WEBSITEV9__DB_CONNECTION
#   HVO_SECRET__WEBSITEPLAYGROUND__DB_CONNECTION
#   HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY
#   HVO_SECRET__WEBSITEPLAYGROUND__AZDO_PAT
#   HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY
#   HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY

apply_secret "src/HVO.WebSite.v9/HVO.WebSite.v9.csproj" \
              "ConnectionStrings:HualapaiValleyObservatory" \
              "HVO_SECRET__WEBSITEV9__DB_CONNECTION"

apply_secret "src/HVO.WebSite.Playground/HVO.WebSite.Playground.csproj" \
              "ConnectionStrings:HualapaiValleyObservatory" \
              "HVO_SECRET__WEBSITEPLAYGROUND__DB_CONNECTION"

apply_secret "src/HVO.WebSite.Playground/HVO.WebSite.Playground.csproj" \
              "NinaApiClient:ApiKey" \
              "HVO_SECRET__WEBSITEPLAYGROUND__NINA_API_KEY"

apply_secret "src/HVO.WebSite.Playground/HVO.WebSite.Playground.csproj" \
              "AzureDevOps:PersonalAccessToken" \
              "HVO_SECRET__WEBSITEPLAYGROUND__AZDO_PAT"

apply_secret "src/HVO.SkyMonitorV5.RPi/HVO.SkyMonitorV5.RPi.csproj" \
              "FrameExport:Raw:S3:0:AccessKey" \
              "HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_ACCESSKEY"

apply_secret "src/HVO.SkyMonitorV5.RPi/HVO.SkyMonitorV5.RPi.csproj" \
              "FrameExport:Raw:S3:0:SecretKey" \
              "HVO_SECRET__SKYMONITORV5__FRAMEEXPORT_RAW_S3_SECRETKEY"

echo "[user-secrets] Completed provisioning (missing secrets were skipped)."
