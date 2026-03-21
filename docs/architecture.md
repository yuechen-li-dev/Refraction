# Refraction M0a architecture

## System overview

Refraction M0a is a two-part web application: a React frontend for the host and viewer UX, and an ASP.NET Core minimal API that creates ephemeral room sessions, tracks lightweight room state in memory, and mints narrowly scoped LiveKit tokens. The host captures display video with the browser screen-capture API, publishes that video into a LiveKit room, and viewers join the same room as passive subscribers through a slug-based link.

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
- Return explicit room state.
- Mint a fresh viewer token when the room is still active.
- Return a clean not-found payload for invalid links.

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

### `POST /api/rooms/{slug}/end`

Responsibilities:

- Explicitly mark a room ended when the host stops sharing.

## Room/session lifecycle summary

1. Host opens `/`.
2. Host clicks **Share screen**.
3. Browser display capture prompt appears.
4. Frontend obtains a display `MediaStream`.
5. Frontend calls `POST /api/rooms`.
6. Backend creates an in-memory session and ensures the LiveKit room exists.
7. Backend returns host token, slug, and viewer URL.
8. Host connects to LiveKit and publishes the display video track.
9. Host calls `POST /api/rooms/{slug}/state` with `live`.
10. Viewer opens `/r/:slug`.
11. Viewer calls `GET /api/rooms/{slug}` and receives a fresh viewer token.
12. Viewer joins LiveKit as a subscriber only.
13. Viewer either:
    - waits for a published host track, or
    - immediately starts rendering live video.
14. Host stops sharing or the display track ends.
15. Host disconnects and calls `POST /api/rooms/{slug}/end`.
16. Viewer sees the stream end through LiveKit disconnect behavior and backend state polling.

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

### Viewer-side UI states

- `loading`
- `resolving_room`
- `joining`
- `waiting_for_host`
- `live`
- `ended`
- `disconnected`
- `error`

### Backend room states

- `waiting`
- `live`
- `ended`
- `error`

## Session/state storage

M0a uses a process-local in-memory room store backed by a concurrent dictionary. There is no database and no durable persistence layer. This keeps the architecture explicit and minimal, but means process restarts invalidate active sessions.

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

This keeps the M0a product contract explicit: viewers are passive subscribers only.

## Out of scope for M0a

- Authentication or user accounts.
- Chat or any collaboration surface.
- Audio.
- Camera or microphone capture.
- Recording, archives, or playback.
- Persistence/database-backed sessions.
- Mobile broadcaster support.
- Multi-host controls, moderation, or richer room management.
- Extra product abstractions for future offerings.
