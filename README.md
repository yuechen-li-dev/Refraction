# Refraction

Refraction is a brutally minimal one-way screen streaming app. A host opens the app, clicks **Share screen**, gets a link, and a viewer opens that link to watch the live screen stream in a browser. M0a intentionally excludes chat, auth, audio, recording, camera, microphone, and broader collaboration features.

## Milestone: M0a

This repository implements the first working slice:

- Host route at `/`.
- Viewer route at `/r/:slug`.
- ASP.NET Core minimal API for ephemeral room creation and viewer resolution.
- Server-side LiveKit token minting.
- In-memory session state only.
- Local happy-path validation target: 1 host + 1 viewer.

## Tech stack

- Frontend: React + Vite + TypeScript.
- Backend: ASP.NET Core minimal API on .NET 8.
- Media transport: LiveKit.
- Session model: ephemeral room-per-link.

## Supported browsers

M0a officially targets **desktop Chromium browsers first** for the host flow, because screen capture support is the product-critical path.

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

Set these in your shell before running the API:

- `LIVEKIT_URL`: public WebSocket URL for your LiveKit deployment, for example `wss://your-project.livekit.cloud`.
- `LIVEKIT_API_KEY`: LiveKit API key.
- `LIVEKIT_API_SECRET`: LiveKit API secret.
- `PUBLIC_APP_BASE_URL`: public URL where the React app is served locally, usually `http://localhost:5173`.
- `CORS_ALLOWED_ORIGINS`: comma-separated allowed frontend origins. For local dev this can stay `http://localhost:5173`.

### Frontend: `frontend/Refraction.App/.env.example`

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

### 2. Run the backend

```bash
cd /workspace/Refraction/backend/Refraction.Api
/root/.dotnet/dotnet run --urls http://localhost:5057
```

If you have `dotnet` on your PATH already, `dotnet run --urls http://localhost:5057` is equivalent.

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
8. Stop screen sharing from the app or the browser capture UI.
9. Confirm the viewer transitions to an ended/disconnected state.

## What works now

- Host can request browser display capture.
- Canceling capture returns the host UI to a usable state with a readable error.
- Backend creates an ephemeral room session and mints scoped host/viewer LiveKit tokens.
- Host gets a viewer URL.
- Viewer resolves room state and joins as a passive subscriber only.
- Viewer shows waiting, live, ended, disconnected, and error states.
- Invalid room links return a sane backend error and surface a readable viewer error.
- Host stop/end flow marks the room ended and viewers poll for the ended state.
- Viewer refresh during an active session is supported by resolving the slug again and joining with a fresh viewer token.

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

### `POST /api/rooms/{slug}/state`

Used by the host client to mark a stream `live` or `ended`.

### `POST /api/rooms/{slug}/end`

Explicit end call used when the host stops sharing.

## Validation commands

### Backend

```bash
cd /workspace/Refraction
/root/.dotnet/dotnet build Refraction.sln
/root/.dotnet/dotnet test Refraction.sln
```

### Frontend

```bash
cd /workspace/Refraction/frontend/Refraction.App
npm install
npm run lint
npm run build
```

## Known limitations

- M0a depends on an already-running LiveKit deployment; this repo does not provision LiveKit.
- Session state is in-memory only in the ASP.NET API. Restarting the API invalidates active slugs.
- Viewers learn that a stream ended via backend polling plus LiveKit disconnect behavior, not a dedicated realtime control channel.
- Frontend production build currently emits a chunk-size warning because `livekit-client` and the app ship in a single main bundle. This does not block the M0a happy path.
- Mobile broadcasting is out of scope.
- No auth, chat, audio, recording, or persistence in M0a.

## Likely follow-up items for M0b

- Harden room cleanup and expiration behavior.
- Improve viewer end-state signaling without polling.
- Optional packaged local dev orchestration for LiveKit.
- Add broader browser support and more deliberate reconnect handling.

## Repository layout

- `frontend/Refraction.App`: React + Vite client.
- `backend/Refraction.Api`: ASP.NET Core minimal API.
- `backend/Refraction.Api.Tests`: backend unit tests.
- `docs/architecture.md`: M0a architecture notes.
- `scripts/run-local.sh`: convenience launcher outline.
