#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${LIVEKIT_URL:-}" || -z "${LIVEKIT_API_KEY:-}" || -z "${LIVEKIT_API_SECRET:-}" ]]; then
  echo "LIVEKIT_URL, LIVEKIT_API_KEY, and LIVEKIT_API_SECRET must be set before running this script."
  exit 1
fi

export PUBLIC_APP_BASE_URL="${PUBLIC_APP_BASE_URL:-http://localhost:5173}"
export CORS_ALLOWED_ORIGINS="${CORS_ALLOWED_ORIGINS:-http://localhost:5173}"
export VITE_API_BASE_URL="${VITE_API_BASE_URL:-http://localhost:5057}"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

(
  cd "$ROOT_DIR/backend/Refraction.Api"
  /root/.dotnet/dotnet run --urls http://localhost:5057
) &
BACKEND_PID=$!

cleanup() {
  kill "$BACKEND_PID" 2>/dev/null || true
}
trap cleanup EXIT

cd "$ROOT_DIR/frontend/Refraction.App"
npm install
npm run dev -- --host 0.0.0.0 --port 5173
