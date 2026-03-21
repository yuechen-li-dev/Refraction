#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_PORT="${API_PORT:-5057}"
APP_PORT="${APP_PORT:-5173}"
DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
if [[ -z "$DOTNET_BIN" && -x "$HOME/.dotnet/dotnet" ]]; then
  DOTNET_BIN="$HOME/.dotnet/dotnet"
fi
NPM_BIN="${NPM_BIN:-$(command -v npm || true)}"

load_env_file() {
  local path="$1"
  if [[ -f "$path" ]]; then
    set -a
    # shellcheck disable=SC1090
    source "$path"
    set +a
  fi
}

load_env_file "$ROOT_DIR/backend/Refraction.Api/.env"
load_env_file "$ROOT_DIR/frontend/Refraction.App/.env"

if [[ -z "$DOTNET_BIN" ]]; then
  echo "dotnet was not found on PATH. Install the .NET 8 SDK or set DOTNET_BIN to your dotnet executable."
  exit 1
fi

if [[ -z "$NPM_BIN" ]]; then
  echo "npm was not found on PATH. Install Node.js 20+ and npm 11+, or set NPM_BIN to your npm executable."
  exit 1
fi

export PUBLIC_APP_BASE_URL="${PUBLIC_APP_BASE_URL:-http://localhost:${APP_PORT}}"
export CORS_ALLOWED_ORIGINS="${CORS_ALLOWED_ORIGINS:-http://localhost:${APP_PORT}}"
export VITE_API_BASE_URL="${VITE_API_BASE_URL:-http://localhost:${API_PORT}}"

if [[ -z "${LIVEKIT_URL:-}" || -z "${LIVEKIT_API_KEY:-}" || -z "${LIVEKIT_API_SECRET:-}" ]]; then
  echo "LIVEKIT_URL, LIVEKIT_API_KEY, and LIVEKIT_API_SECRET must be set before running this script."
  echo "Tip: copy backend/Refraction.Api/.env.example to backend/Refraction.Api/.env and fill it in, or export the values in your shell first."
  exit 1
fi

echo "Starting Refraction local run"
echo "  Frontend: http://localhost:${APP_PORT}"
echo "  Backend:  http://localhost:${API_PORT}"
echo "  API base: ${VITE_API_BASE_URL}"

(
  cd "$ROOT_DIR/backend/Refraction.Api"
  "$DOTNET_BIN" run --urls "http://localhost:${API_PORT}"
) &
BACKEND_PID=$!

cleanup() {
  kill "$BACKEND_PID" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

cd "$ROOT_DIR/frontend/Refraction.App"
"$NPM_BIN" install
exec "$NPM_BIN" run dev -- --host 0.0.0.0 --port "$APP_PORT"
