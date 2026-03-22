# Refraction

Refraction is a brutally minimal one-way screen streaming app. A host opens the app, clicks **Share screen**, gets a link, and viewers open that link to watch the live screen stream in a browser.

M0, M0.5, M0c, and M1 delivered the narrow product slice and hardening pass. M2 keeps the same product shape while adding one minimal public deployment path.

## Milestone status: M2

The implementation remains intentionally narrow:

- Host route at `/`.
- Viewer route at `/r/:slug`.
- ASP.NET Core minimal API for ephemeral room creation, viewer resolution, and room state transitions.
- Server-side LiveKit token minting.
- In-memory session state only, with explicit cleanup and expiration rules.
- Local validation target: **1 host + many passive viewers**.
- Public deployment target: **1 VPS + external LiveKit + public HTTPS URL**.

## Tech stack

- Frontend: React + Vite + TypeScript.
- Backend: ASP.NET Core minimal API on .NET 8.
- Media transport: LiveKit.
- Session model: ephemeral room-per-link.

## Supported browsers

Refraction targets **desktop Chromium browsers first** for the host flow, because screen capture support is the product-critical path.

## Prerequisites

- Node.js 20+.
- npm 11+.
- .NET 8 SDK.
- A reachable LiveKit deployment, either:
  - LiveKit Cloud, or
  - a self-hosted LiveKit server.
- Multiple desktop Chromium browser contexts for demoing host and viewers locally.

## Single blessed fresh-local-run path

This is the one local workflow the repo now treats as canonical.

### 1. Copy the env templates

```bash
cd /workspace/Refraction
cp backend/Refraction.Api/.env.example backend/Refraction.Api/.env
cp frontend/Refraction.App/.env.example frontend/Refraction.App/.env
```

### 2. Edit `backend/Refraction.Api/.env`

Set the required backend values:

```dotenv
LIVEKIT_URL=wss://your-livekit-host
LIVEKIT_API_KEY=your-livekit-api-key
LIVEKIT_API_SECRET=your-livekit-api-secret
PUBLIC_APP_BASE_URL=http://localhost:5173
CORS_ALLOWED_ORIGINS=http://localhost:5173
```

You can keep the session lifecycle defaults unless you are intentionally testing expiry behavior:

```dotenv
ROOM_SESSION_WAITING_TTL_MINUTES=15
ROOM_SESSION_LIVE_TTL_MINUTES=240
ROOM_SESSION_ENDED_RETENTION_MINUTES=10
ROOM_SESSION_CLEANUP_INTERVAL_SECONDS=30
```

### 3. Confirm `frontend/Refraction.App/.env`

For the default local setup, this should stay:

```dotenv
VITE_API_BASE_URL=http://localhost:5057
```

### 4. Run the helper script

```bash
cd /workspace/Refraction
./scripts/run-local.sh
```

What the script does:

- sources `backend/Refraction.Api/.env` if present,
- sources `frontend/Refraction.App/.env` if present,
- uses backend `http://localhost:5057`,
- uses frontend `http://localhost:5173`,
- starts the ASP.NET Core API with `dotnet run`,
- runs `npm install`,
- starts the Vite dev server.

If you stay on the default ports above, the README, env templates, backend launch settings, frontend fallback API URL, and `scripts/run-local.sh` all match.

## Public deployment (M2 blessed path)

M2 uses **one recommended deployment path only**:

- **One VPS**
- **Docker Compose**
- **One ASP.NET Core backend container**
- **One Caddy container that serves the frontend build and reverse-proxies `/api/*` to the backend**
- **One external LiveKit instance** (recommended: LiveKit Cloud)

This keeps deployment single-node and repeatable while still producing a real public HTTPS URL.

### Deployment topology

- `https://<your-domain>/` → React frontend served by Caddy
- `https://<your-domain>/api/*` → ASP.NET Core backend via Caddy reverse proxy
- Browser host/viewer media connection → external LiveKit instance at `LIVEKIT_URL`

### Why external LiveKit is the blessed M2 choice

For M2, Refraction expects **LiveKit to be provided externally** rather than self-hosted on the VPS. That keeps the proof deployment narrow:

- the VPS only runs Refraction itself,
- the backend still mints host/viewer tokens using `LIVEKIT_URL`, `LIVEKIT_API_KEY`, and `LIVEKIT_API_SECRET`,
- the browser connects directly to that hosted LiveKit instance.

### Deployment prerequisites

- A Linux VPS with:
  - Docker Engine
  - Docker Compose plugin
- A public DNS record pointing your domain to that VPS
- Ports `80` and `443` open to the internet
- A LiveKit Cloud project or another externally reachable LiveKit instance

### Files used by the deployment path

- `deploy/docker-compose.yml`
- `deploy/.env.example`
- `backend/Refraction.Api/Dockerfile`
- `frontend/Refraction.App/Dockerfile`
- `frontend/Refraction.App/Caddyfile`

### 1. Copy the deployment env file

```bash
cd /workspace/Refraction
cp deploy/.env.example deploy/.env
```

### 2. Edit `deploy/.env`

Set every required public value:

```dotenv
DOMAIN=refraction.example.com
ACME_EMAIL=you@example.com
LIVEKIT_URL=wss://your-project.livekit.cloud
LIVEKIT_API_KEY=your-livekit-api-key
LIVEKIT_API_SECRET=your-livekit-api-secret
PUBLIC_APP_BASE_URL=https://refraction.example.com
CORS_ALLOWED_ORIGINS=https://refraction.example.com
VITE_API_BASE_URL=
```

Important notes:

- `PUBLIC_APP_BASE_URL` must be the exact public frontend origin the user opens in the browser.
- `CORS_ALLOWED_ORIGINS` should match that same public frontend origin.
- Leave `VITE_API_BASE_URL` empty for the recommended same-origin deployment. In that mode, the frontend calls `https://<your-domain>/api/...` through Caddy.
- `LIVEKIT_URL` must be the **public** WebSocket URL for your hosted LiveKit instance.

### 3. Build and start the public deployment

```bash
cd /workspace/Refraction
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d --build
```

### 4. Verify the deployment

Expected URLs:

- App: `https://refraction.example.com/`
- Health endpoint: `https://refraction.example.com/api/health`
- Viewer links minted by the backend: `https://refraction.example.com/r/<slug>`

Check the API:

```bash
curl https://refraction.example.com/api/health
```

Expected response:

```json
{"status":"ok"}
```

### 5. Updating the deployment

After pulling new changes:

```bash
cd /workspace/Refraction
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d --build
```

### HTTPS details

The blessed deployment uses **Caddy** as the reverse proxy and HTTPS terminator.

- Caddy listens on ports `80` and `443`.
- Caddy automatically provisions and renews Let’s Encrypt certificates for `DOMAIN`.
- The frontend and API are both exposed through the same public origin.

No extra manual nginx or certbot steps are required for the recommended M2 path.

### Exact demo flow to validate M2

Use this flow to confirm the public deployment is working end-to-end across different networks:

1. On machine A, open `https://<your-domain>/`.
2. Click **Share screen**.
3. Grant browser screen-capture permission.
4. Wait for the host status to reach **Live**.
5. Copy the generated viewer link, which should look like `https://<your-domain>/r/<slug>`.
6. On machine B, using a different browser or different network, open that viewer link.
7. Confirm the viewer sees the host’s screen.
8. Stop sharing on the host.
9. Confirm the viewer transitions to the ended state instead of hanging on a stale live screen.

### M2 limitations

This deployment is intentionally minimal and keeps all existing non-goals:

- no auth
- no audio
- no chat
- no recording
- no persistence/database
- no autoscaling
- no Kubernetes
- no observability stack

The backend still stores room/session state **in memory only**, so restarting the backend clears active/ended sessions.

## Environment details

### Backend: `backend/Refraction.Api/.env.example`

This file is a template only. ASP.NET Core does **not** load it automatically in this repo. The blessed run path works because `scripts/run-local.sh` sources it before starting the API.

Required values:

- `LIVEKIT_URL`: public WebSocket URL for your LiveKit deployment, for example `wss://your-project.livekit.cloud`.
- `LIVEKIT_API_KEY`: LiveKit API key.
- `LIVEKIT_API_SECRET`: LiveKit API secret.
- `PUBLIC_APP_BASE_URL`: public URL where the React app is served. Local default: `http://localhost:5173`. Public deployment example: `https://refraction.example.com`.
- `CORS_ALLOWED_ORIGINS`: comma-separated allowed frontend origins. Local default: `http://localhost:5173`. Public deployment example: `https://refraction.example.com`.

Optional session lifecycle overrides:

- `ROOM_SESSION_WAITING_TTL_MINUTES`: how long an unstarted room link survives before expiring. Default: `15`.
- `ROOM_SESSION_LIVE_TTL_MINUTES`: maximum time an active room can remain live before the backend auto-ends it. Default: `240`.
- `ROOM_SESSION_ENDED_RETENTION_MINUTES`: how long an ended room remains resolvable as `ended` before being removed. Default: `10`.
- `ROOM_SESSION_CLEANUP_INTERVAL_SECONDS`: how often the API sweeps expired sessions. Default: `30`.

### Frontend: `frontend/Refraction.App/.env.example`

Vite loads frontend `.env` files automatically in local development, and `scripts/run-local.sh` also sources the file so the script path and direct `npm run dev` path stay aligned.

- `VITE_API_BASE_URL`: backend base URL. Default local value: `http://localhost:5057`.
- In the blessed public deployment, leave `VITE_API_BASE_URL` unset so the production build uses the browser origin and reaches the API through the reverse-proxied `/api` path on the same public domain.

## Manual smoke-test checklist

Use this after starting the app locally:

- [ ] Host opens `http://localhost:5173/` and clicks **Share screen**.
- [ ] Browser capture permission is granted and the host status reaches **Live**.
- [ ] Viewer A opens the generated `/r/:slug` link.
- [ ] Viewer B opens the same `/r/:slug` link concurrently.
- [ ] Both viewers see the host screen live.
- [ ] One viewer refreshes during the active session and rejoins correctly without affecting the other viewer.
- [ ] Host stops sharing from the app or browser capture UI.
- [ ] Viewer sees the ended/dead terminal state rather than a hanging live state.
- [ ] An invalid or fully expired link shows a sane terminal message.

## Session lifecycle and cleanup behavior

The API keeps sessions only in memory, with explicit lifecycle rules:

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

Resolves a viewer link and returns a fresh passive-viewer token for each active viewer join attempt:

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
- Multiple concurrent viewers can resolve the same slug independently; each successful resolve receives its own subscribe-only token for the same room.
- After the ended-retention window passes, the same slug returns a `room_not_found` error because the dead session has been cleaned up.

### `POST /api/rooms/{slug}/state`

Used by the host client to mark a stream `live` or `ended`.

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

## Requirement / status evidence table

### M0 requirements

| Requirement | Status | Evidence / notes |
| --- | --- | --- |
| Host can start one-way desktop screen sharing from `/`. | Complete | Host flow is implemented in the React host page using browser display capture plus LiveKit publish. |
| Host receives a viewer link for the active session. | Complete | Room creation returns `viewerUrl`, and the host UI exposes copyable link output. |
| Viewer can open `/r/:slug` and watch passively. | Complete | Viewer route resolves the slug, joins with a viewer token, and subscribes without any upstream controls. |
| Local run works with a minimal backend + frontend setup. | Complete | README, env templates, backend defaults, frontend defaults, and `scripts/run-local.sh` now all point at the same `5173`/`5057` local path. |
| Scope stays intentionally narrow. | Complete | No auth, chat, audio, recording, persistence, multi-host, mobile, or deployment workflow is added. |

### M0.5 requirements

| Requirement | Status | Evidence / notes |
| --- | --- | --- |
| Waiting, live, ended, and invalid/expired room states are explicit. | Complete | API responses and viewer UI differentiate waiting/live/ended/error states with terminal copy. |
| Viewer refresh during an active session rejoins cleanly. | Complete | Viewer page re-resolves the slug and reconnects with a fresh viewer token. |
| Ended rooms do not look live forever. | Complete | Host stop, disconnect, page exit, explicit end, and maximum live TTL all converge on ended state handling. |
| Dead links resolve to a sane terminal state. | Complete | Missing/expired rooms return `room_not_found`, and the viewer lands in a terminal ended/error-style state instead of spinning forever. |
| In-memory room state is bounded and cleaned up. | Complete | Waiting TTL, live TTL, ended retention, and background cleanup make process-local storage auditable and finite. |
| Local validation is explicit and repeatable. | Complete | README lists the exact backend/frontend validation commands plus a manual smoke-test checklist. |

### M1 requirements

| Requirement | Status | Evidence / notes |
| --- | --- | --- |
| One host can serve many concurrent passive viewers through the same link. | Complete | Repeated room resolution for the same slug now remains an explicit supported path, with a fresh subscribe-only viewer token minted for each active join attempt. |
| Existing waiting/live/ended/dead behavior remains intact. | Complete | M1 keeps the same frontend state machine and backend lifecycle semantics; it only broadens the existing passive-viewer path to multiple concurrent viewers. |
| Permission boundaries stay narrow. | Complete | Viewer tokens remain subscribe-only and host tokens remain publish-only. |

### M2 requirements

| Requirement | Status | Evidence / notes |
| --- | --- | --- |
| One blessed public deployment path exists. | Complete | README now documents a single Docker Compose deployment flow for one VPS with exact commands and expected URLs. |
| Frontend and backend are publicly reachable over HTTPS. | Complete | Caddy serves the frontend on the public domain and reverse-proxies `/api/*` to the backend while handling Let’s Encrypt certificates. |
| Backend and frontend configuration is explicit. | Complete | Backend, frontend, and deployment env templates now spell out the required LiveKit, URL, and API-base settings with no hidden production-only values. |
| Viewer links work across networks. | Complete | The documented public deployment sets `PUBLIC_APP_BASE_URL` to the real HTTPS origin, so generated `/r/:slug` links are shareable outside localhost. |
| LiveKit integration remains minimal and explicit. | Complete | M2 documents external LiveKit as the blessed deployment assumption, and the deployed backend still mints host/viewer tokens from environment-provided LiveKit credentials. |

## Known limitations after M2

- Refraction still depends on an already-running LiveKit deployment; this repo does not provision LiveKit.
- Session state is still process-local and in-memory only. Restarting the API invalidates active slugs immediately.
- The app still targets exactly one host and passive viewers only.
- No auth, chat, audio, recording, persistence, admin surface, multi-host support, mobile support, autoscaling, or observability stack has been added.
- Frontend production build may still emit a chunk-size warning because `livekit-client` and the app ship in the same main bundle.

## Repository layout

- `frontend/Refraction.App`: React + Vite client.
- `backend/Refraction.Api`: ASP.NET Core minimal API.
- `backend/Refraction.Api.Tests`: backend unit tests.
- `docs/architecture.md`: architecture notes for the hardened slice.
- `scripts/run-local.sh`: blessed local runner.
