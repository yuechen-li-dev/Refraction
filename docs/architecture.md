# Refraction M1 architecture

## System overview

Refraction keeps the same two-part architecture established in M0a and hardened through M0c: a React frontend for the host and viewer UX, and an ASP.NET Core minimal API that creates ephemeral room sessions, tracks lightweight room state in memory, and mints narrowly scoped LiveKit tokens. The host captures display video with the browser screen-capture API, publishes that video into a LiveKit room, and viewers join the same room as passive subscribers through a slug-based link.

M1 does not redesign the product. It keeps the same host flow, the same viewer link flow, and the same lifecycle semantics, while allowing multiple concurrent passive viewers to resolve and join the same host session independently.

## Route summary

### Frontend routes

- `/`: host page.
- `/r/:slug`: viewer page.

### Backend routes

- `GET /api/health`: simple health check.
- `POST /api/rooms`: create a new ephemeral room session.
- `GET /api/rooms/{slug}`: resolve a viewer-facing room slug.
- `POST /api/rooms/{slug}/state`: mark a room `live` or `ended` from the host client.
- `POST /api/rooms/{slug}/end`: explicitly end a room session.

## Backend endpoint summary

### `POST /api/rooms`

Responsibilities:

- Create a new in-memory room session.
- Generate a stable `roomId` and shareable `roomSlug`.
- Opportunistically sweep expired sessions before adding a new one.
- Ensure the LiveKit room exists.
- Mint a host token with publish permission only.
- Return the viewer URL and LiveKit connection URL.

Response shape:

- `roomId`
- `roomSlug`
- `hostToken`
- `viewerUrl`
- `liveKitUrl`
- `state`

### `GET /api/rooms/{slug}`

Responsibilities:

- Validate the slug.
- Normalize the session lifecycle before returning it.
- Auto-end stale live rooms that exceeded the configured live TTL.
- Mint a fresh viewer token for each active viewer resolve when the room is still active.
- Return a clean not-found payload for invalid or fully expired links.

Response shape:

- `roomId`
- `roomSlug`
- `viewerToken`
- `liveKitUrl`
- `state`
- `message`

### `POST /api/rooms/{slug}/state`

Responsibilities:

- Let the host mark the room `live` after the screen track is published.
- Let the host mark the room `ended` if needed.
- Return explicit failure semantics for unsupported, stale, or invalid state transitions.
- Refuse to resurrect already ended sessions back to `live`.

### `POST /api/rooms/{slug}/end`

Responsibilities:

- Explicitly mark a room ended when the host stops sharing.
- Support best-effort cleanup when the host session fails after slug creation but before steady-state streaming.

## Room/session lifecycle summary

1. Host opens `/`.
2. Host clicks **Share screen**.
3. Browser display capture prompt appears.
4. Frontend obtains a display `MediaStream`.
5. Frontend calls `POST /api/rooms`.
6. Backend creates an in-memory session in `waiting` and ensures the LiveKit room exists.
7. Backend returns host token, slug, and viewer URL.
8. Host connects to LiveKit and publishes the display video track.
9. Host calls `POST /api/rooms/{slug}/state` with `live`.
10. Viewer opens `/r/:slug`.
11. Each viewer calls `GET /api/rooms/{slug}` and receives a fresh viewer token if the room is still active.
12. Each viewer joins LiveKit as a subscriber only.
13. Each viewer either:
    - waits for a published host track, or
    - immediately starts rendering live video.
14. While active, each viewer runs lightweight backend polling to keep backend session state and LiveKit state aligned.
15. If LiveKit disconnects but the backend still reports an active room, that viewer re-resolves the slug and reconnects with a fresh viewer token.
16. Other viewers do not mutate one another's backend state; they only consume the same room metadata and passive access path.
17. If the host stops sharing, closes the page, or the browser ends the display track, the host page triggers backend end cleanup (including a best-effort page-exit beacon).
18. The session transitions to `ended`, viewers show a human-readable ended state, and the slug remains resolvable for the configured ended-retention window.
19. After that retention window, background cleanup removes the dead session and the slug becomes invalid/expired.

## State model summary

### Host-side UI states

- `idle`
- `requesting_capture`
- `creating_room`
- `connecting`
- `live`
- `stopping`
- `ended`
- `error`

Notes:

- Unexpected LiveKit disconnects now route through the same teardown path as a normal stop so the backend is told the room ended when possible.
- Page-exit handling also sends a best-effort `/end` request so viewers get an ended/dead answer sooner after normal tab closes.
- Failures after room creation use best-effort `/end` cleanup instead of leaving the slug stranded in `waiting` until TTL expiry.

### Viewer-side UI states

- `loading`
- `resolving_room`
- `joining`
- `waiting_for_host`
- `live`
- `ended`
- `disconnected`
- `error`

Notes:

- `resolving_room` is now reused deliberately when the viewer loses the LiveKit connection and needs to confirm whether the room truly ended or should be rejoined.
- `disconnected` means the viewer temporarily lost backend reachability during an otherwise still-active session and is retrying.
- `ended` means the backend has definitively declared the room ended or no longer issues viewer tokens for it.

### Backend room states

- `waiting`
- `live`
- `ended`
- `error`

## Session/state storage

Refraction still uses a process-local in-memory room store backed by a concurrent dictionary. There is still no database and no durable persistence layer. The hardened slice adds an explicit lifecycle policy around that store:

- Waiting sessions expire after `ROOM_SESSION_WAITING_TTL_MINUTES` (default 15 minutes).
- Live sessions auto-transition to `ended` after `ROOM_SESSION_LIVE_TTL_MINUTES` (default 240 minutes).
- Ended sessions remain queryable for `ROOM_SESSION_ENDED_RETENTION_MINUTES` (default 10 minutes).
- A background cleanup service runs every `ROOM_SESSION_CLEANUP_INTERVAL_SECONDS` (default 30 seconds).
- Request-time normalization enforces the same rules even if a cleanup sweep has not run yet.

This means room links do not accumulate forever, abandoned rooms stop presenting as live forever, and recently ended rooms still have a clear grace window for viewer feedback.

## Viewer end/disconnect semantics

The current slice intentionally keeps the signaling path simple:

- The backend remains the source of truth for whether a session is `waiting`, `live`, or `ended`.
- The viewer still listens to LiveKit track and disconnect events for immediate media feedback.
- A bounded polling loop re-resolves the room every few seconds only while each viewer is in an active session flow.
- On LiveKit disconnect or track loss, a viewer does not immediately assume the session ended. It first rechecks backend session state.
- If the backend now reports `ended` or `room_not_found`, the viewer moves into a terminal ended/dead state instead of looping reconnect attempts.
- If the backend still reports an active room, the viewer reconnects with a fresh viewer token.

This is less clever than a dedicated realtime control channel, but it is explicit, easy to reason about, and aligned with the current architecture.

## M1 multi-viewer notes

- The backend room store still tracks session lifecycle only; it does not maintain a participant roster or presence system.
- Multiple viewers can resolve the same slug concurrently because `GET /api/rooms/{slug}` is a read-style operation over shared session state plus fresh viewer-token minting.
- Viewer tokens remain subscribe-only, so passive viewers cannot publish tracks upstream or interfere with the host stream.
- The LiveKit room creation path now uses a larger participant ceiling so the same link can support more than the original narrow demo pairing without changing the product model.

## Local development defaults

- Backend default local URL: `http://localhost:5057`.
- Frontend default local URL: `http://localhost:5173`.
- `scripts/run-local.sh` uses those same defaults, sources optional `.env` files for convenience, and relies on `dotnet` from PATH (or `DOTNET_BIN`) instead of a hardcoded absolute path.

## LiveKit permission model

- Host token:
  - room join allowed.
  - publish allowed.
  - subscribe denied.
  - data publish denied.
- Viewer token:
  - room join allowed.
  - publish denied.
  - subscribe allowed.
  - data publish denied.

This keeps the product contract explicit: viewers are passive subscribers only.

## Remaining limitations after M1

- Process restarts still invalidate active sessions because there is no persistence layer.
- There is still no dedicated realtime control plane for session-end signaling.
- There is still no authentication, chat, audio, recording, or broader room-management surface.
- Live-session auto-ending uses a maximum TTL, not a heartbeat-driven inactivity detector.
- Mobile broadcasting and multi-host scenarios remain out of scope.
