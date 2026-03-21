export type HostState =
  | 'idle'
  | 'requesting_capture'
  | 'creating_room'
  | 'connecting'
  | 'live'
  | 'stopping'
  | 'ended'
  | 'error'

export type ViewerState =
  | 'loading'
  | 'resolving_room'
  | 'joining'
  | 'waiting_for_host'
  | 'live'
  | 'ended'
  | 'disconnected'
  | 'error'

export type RoomState = 'waiting' | 'live' | 'ended' | 'error'

export interface CreateRoomResponse {
  roomId: string
  roomSlug: string
  hostToken: string
  viewerUrl: string
  liveKitUrl: string
  state: RoomState
}

export interface ResolveRoomResponse {
  roomId: string
  roomSlug: string
  viewerToken: string | null
  liveKitUrl: string
  state: RoomState
  message: string | null
}

export interface ApiErrorResponse {
  code: string
  message: string
  state: RoomState
}
