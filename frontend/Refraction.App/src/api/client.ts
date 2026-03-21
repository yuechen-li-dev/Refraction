import type { ApiErrorResponse, CreateRoomResponse, ResolveRoomResponse, RoomState } from '../types/app'

export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? 'http://localhost:5057'

export class ApiRequestError extends Error {
  readonly code: string
  readonly state: RoomState
  readonly status: number

  constructor(status: number, error: ApiErrorResponse) {
    super(error.message || 'Request failed.')
    this.name = 'ApiRequestError'
    this.code = error.code
    this.state = error.state
    this.status = status
  }
}

async function readJson<T>(response: Response): Promise<T> {
  const body = (await response.json()) as T | ApiErrorResponse

  if (!response.ok) {
    const error = body as ApiErrorResponse
    throw new ApiRequestError(response.status, {
      code: error.code || 'request_failed',
      message: error.message || 'Request failed.',
      state: error.state || 'error',
    })
  }

  return body as T
}

export async function createRoom(): Promise<CreateRoomResponse> {
  const response = await fetch(`${apiBaseUrl}/api/rooms`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({}),
  })

  return readJson<CreateRoomResponse>(response)
}

export async function resolveRoom(slug: string): Promise<ResolveRoomResponse> {
  const response = await fetch(`${apiBaseUrl}/api/rooms/${slug}`)
  return readJson<ResolveRoomResponse>(response)
}

export async function updateRoomState(slug: string, state: RoomState): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/rooms/${slug}/state`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ state }),
  })

  await readJson(response)
}

export async function endRoom(slug: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/api/rooms/${slug}/end`, {
    method: 'POST',
  })

  await readJson(response)
}
