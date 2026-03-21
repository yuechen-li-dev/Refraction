# Refraction

Refraction is a brutally minimal one-way screen streaming app. A host opens the app, clicks **Share screen**, gets a link, and a viewer opens that link to watch the live screen stream in a browser. M0b keeps the same narrow product surface as M0a and focuses only on making that demo path harder to embarrass in live use.

## Milestone: M0b demo hardening

This repository now implements the hardened demo slice:

- Host route at `/`.
- Viewer route at `/r/:slug`.
- ASP.NET Core minimal API for ephemeral room creation and viewer resolution.
- Server-side LiveKit token minting.
- In-memory session state only, with explicit cleanup and expiration rules.
- Local validation target: 1 host + passive viewers.

## Tech stack

- Frontend: React + Vite + TypeScript.
- Backend: ASP.NET Core minimal API on .NET 8.
- Media transport: LiveKit.
- Session model: ephemeral room-per-link.

## Supported browsers

Refraction still targets **desktop Chromium browsers first** for the host flow, because screen capture support is the product-critical path.

## Prerequisites

- Node.js 20+.
- npm 11+.
- .NET 8 SDK.
- A reachable LiveKit deployment, either:
  - LiveKit Cloud, or
  - a self-hosted LiveKit server.
- Two desktop Chromium browser contexts for demoing host and viewer locally.

## Required environment variables

### Backend: `backend/Refraction.Api/.env.example`

This file is a template only. ASP.NET Core does **not** load it automatically in this repo. Either export these values in your shell, or copy it to `backend/Refraction.Api/.env` and source that file before running the API or `scripts/run-local.sh`:

- `LIVEKIT_URL`: public WebSocket URL for your LiveKit deployment, for example `wss://your-project.livekit.cloud`.
- `LIVEKIT_API_KEY`: LiveKit API key.
- `LIVEKIT_API_SECRET`: LiveKit API secret.
- `PUBLIC_APP_BASE_URL`: public URL where the React app is served locally, usually `http://localhost:5173`.
- `CORS_ALLOWED_ORIGINS`: comma-separated allowed frontend origins. For local dev this can stay `http://localhost:5173`.

Optional session-hardening overrides:

- `ROOM_SESSION_WAITING_TTL_MINUTES`: how long an unstarted room link survives before expiring. Default: `15`.
- `ROOM_SESSION_LIVE_TTL_MINUTES`: maximum time an active room can remain live before the backend auto-ends it. Default: `240`.
- `ROOM_SESSION_ENDED_RETENTION_MINUTES`: how long an ended room remains resolvable as `ended` before being removed. Default: `10`.
- `ROOM_SESSION_CLEANUP_INTERVAL_SECONDS`: how often the API sweeps expired sessions. Default: `30`.

### Frontend: `frontend/Refraction.App/.env.example`

Vite does load frontend `.env` files automatically in local development.

- `VITE_API_BASE_URL`: backend base URL, usually `http://localhost:5057`.

## Exact local run steps

### 1. Configure environment variables

Example shell session:

```bash
export LIVEKIT_URL="wss://your-livekit-host"
export LIVEKIT_API_KEY="your-livekit-api-key"
export LIVEKIT_API_SECRET="your-livekit-api-secret"
export PUBLIC_APP_BASE_URL="http://localhost:5173"
export CORS_ALLOWED_ORIGINS="http://localhost:5173"
export VITE_API_BASE_URL="http://localhost:5057"
```

Or, for the provided helper script, copy the example files and edit them in place:

```bash
cp backend/Refraction.Api/.env.example backend/Refraction.Api/.env
cp frontend/Refraction.App/.env.example frontend/Refraction.App/.env
```

### 2. Run the backend

```bash
cd /workspace/Refraction/backend/Refraction.Api
dotnet run --urls http://localhost:5057
```

The backend launch profile, frontend fallback API URL, and helper script all use port `5057` by default.

### 3. Run the frontend

In a second terminal:

```bash
cd /workspace/Refraction/frontend/Refraction.App
npm install
npm run dev -- --host 0.0.0.0 --port 5173
```

### 4. Demo the local happy path

1. Open `http://localhost:5173/` in desktop Chromium.
2. Click **Share screen**.
3. Approve browser display capture.
4. Wait for the status to become **Live**.
5. Copy the generated viewer link.
6. Open that link in a second Chromium window, tab, or browser profile.
7. Confirm the viewer sees the host screen.
8. Refresh the viewer while the host is still live and confirm it re-resolves the slug and rejoins cleanly.
9. Stop screen sharing from the app or the browser capture UI.
10. Confirm the viewer lands in the ended state, then later becomes invalid/expired after the retention window passes.

## What is hardened in M0b

- Backend in-memory sessions no longer accumulate forever.
- Waiting sessions expire automatically if the host never goes live.
- Live sessions are auto-ended after a bounded maximum lifetime so abandoned demo rooms stop looking active forever.
- Ended sessions stay resolvable briefly, then are cleaned up into a dead/expired state.
- Viewer refresh during an active session re-resolves the room and rejoins with a fresh viewer token.
- Viewer disconnect handling now rechecks backend room state before deciding whether to show `ended`, retry, or reconnect.
- Host teardown now makes a best-effort end call even when room creation or LiveKit connection fails after the room slug already exists.

## Session lifecycle and cleanup behavior

The API keeps sessions only in memory, but M0b makes that lifecycle explicit:

1. `POST /api/rooms` creates a session in `waiting`.
2. If the host successfully publishes, the frontend marks the room `live`.
3. If the host stops sharing, closes the page, disconnects unexpectedly, or the maximum live TTL elapses, the session transitions to `ended`.
4. Ended sessions remain resolvable for a short retention window so viewers can see a clear ended state.
5. After retention, the slug is removed from memory and resolves as invalid/expired.
6. A background cleanup loop sweeps expired sessions on a fixed interval, and request-time normalization also enforces the same rules.

## Minimal API contract

### `POST /api/rooms`

Creates a room session and returns:

```json
{
  "roomId": "room-...",
  "roomSlug": "abcd2345",
  "hostToken": "<jwt>",
  "viewerUrl": "http://localhost:5173/r/abcd2345",
  "liveKitUrl": "wss://...",
  "state": "waiting"
}
```

### `GET /api/rooms/{slug}`

Resolves a viewer link and returns:

```json
{
  "roomId": "room-...",
  "roomSlug": "abcd2345",
  "viewerToken": "<jwt-or-null>",
  "liveKitUrl": "wss://...",
  "state": "waiting|live|ended|error",
  "message": "..."
}
```

Notes:

- `viewerToken` is `null` once the backend considers the room ended.
- After the ended-retention window passes, the same slug returns a `room_not_found` error because the dead session has been cleaned up.

### `POST /api/rooms/{slug}/state`

Used by the host client to mark a stream `live` or `ended`. Invalid or stale transitions now fail explicitly:

- `400` for unsupported target states.
- `404` when the room is already missing or expired.
- `409` when the requested transition is stale or invalid for the current room state.

### `POST /api/rooms/{slug}/end`

Explicit end call used when the host stops sharing or local teardown wants to clean up a partially created session.

## Validation commands

### Backend

```bash
cd /workspace/Refraction
dotnet build Refraction.sln
dotnet test Refraction.sln
```

### Frontend

```bash
cd /workspace/Refraction/frontend/Refraction.App
npm install
npm run lint
npm run build
```

## Known limitations after M0b

- Refraction still depends on an already-running LiveKit deployment; this repo does not provision LiveKit.
- Session state is still process-local and in-memory only. Restarting the API invalidates active slugs immediately.
- Viewer end/disconnect handling is more deliberate, but it still relies on lightweight backend polling plus LiveKit events rather than a dedicated realtime control channel.
- The app still targets a single host and passive viewers only.
- No auth, chat, audio, recording, persistence, or admin surface has been added.
- Frontend production build may still emit a chunk-size warning because `livekit-client` and the app ship in the same main bundle.

## Convenience local runner

`./scripts/run-local.sh` now keeps the default ports aligned (`5173` frontend, `5057` backend), uses `dotnet` from your PATH or `DOTNET_BIN`, and will source `backend/Refraction.Api/.env` plus `frontend/Refraction.App/.env` if those files exist.

## Repository layout

- `frontend/Refraction.App`: React + Vite client.
- `backend/Refraction.Api`: ASP.NET Core minimal API.
- `backend/Refraction.Api.Tests`: backend unit tests.
- `docs/architecture.md`: M0b architecture notes.
- `scripts/run-local.sh`: convenience launcher outline.
