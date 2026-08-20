#!/usr/bin/env bash
# Smoke: start Minimal sample, GET /hello twice, expect X-Cache miss then hit.
# Usage (from repo root, after Release build):
#   bash samples/CacheOrchestrator.Minimal/smoke.sh
#   CONFIGURATION=Debug bash samples/CacheOrchestrator.Minimal/smoke.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PORT="${SMOKE_PORT:-5290}"
BASE="http://127.0.0.1:${PORT}"
CONFIGURATION="${CONFIGURATION:-Release}"
PID=""

cleanup() {
  if [[ -n "${PID}" ]] && kill -0 "${PID}" 2>/dev/null; then
    kill "${PID}" 2>/dev/null || true
    wait "${PID}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

echo "==> Starting CacheOrchestrator.Minimal on ${BASE} (${CONFIGURATION})"
# ASPNETCORE_URLS overrides launchSettings so CI does not depend on profile binding quirks.
ASPNETCORE_URLS="${BASE}" \
ASPNETCORE_ENVIRONMENT=Development \
  dotnet run \
    --project "${SCRIPT_DIR}/CacheOrchestrator.Minimal.csproj" \
    --configuration "${CONFIGURATION}" \
    --no-build \
    --no-launch-profile \
    >"${SCRIPT_DIR}/smoke.run.log" 2>&1 &
PID=$!

echo "==> Waiting for server (GET / — not cache-domain)"
ready=0
for _ in $(seq 1 60); do
  if curl -sf "${BASE}/" -o /dev/null; then
    ready=1
    break
  fi
  if ! kill -0 "${PID}" 2>/dev/null; then
    echo "Server process exited early. Log:"
    cat "${SCRIPT_DIR}/smoke.run.log" || true
    exit 1
  fi
  sleep 0.5
done

if [[ "${ready}" -ne 1 ]]; then
  echo "Timed out waiting for ${BASE}/"
  cat "${SCRIPT_DIR}/smoke.run.log" || true
  exit 1
fi

echo "==> First /hello (expect MISS)"
H1="$(curl -sD - -o /dev/null "${BASE}/hello")"
echo "${H1}" | tr -d '\r' | grep -i '^x-cache:' || {
  echo "Missing X-Cache on first response:"
  echo "${H1}"
  exit 1
}
echo "${H1}" | tr -d '\r' | grep -iE 'x-cache:.*(oc=miss|fc=miss)' || {
  echo "First response should be a cache miss. Headers:"
  echo "${H1}"
  exit 1
}

echo "==> Second /hello (expect HIT)"
H2="$(curl -sD - -o /dev/null "${BASE}/hello")"
echo "${H2}" | tr -d '\r' | grep -i '^x-cache:' || {
  echo "Missing X-Cache on second response:"
  echo "${H2}"
  exit 1
}
echo "${H2}" | tr -d '\r' | grep -iE 'x-cache:.*oc=hit' || {
  echo "Second response should be oc=hit. Headers:"
  echo "${H2}"
  exit 1
}

echo "==> Minimal sample smoke OK (miss → hit)"
