export type ChatConnectionState = 'idle' | 'connecting' | 'connected' | 'error' | 'closed'

export interface ChatMessage {
  id: string
  roomSlug: string
  kind: 'user' | 'system'
  text: string
  displayName: string | null
  sentAtUtc: string
}

export interface ChatClosedEvent {
  roomSlug: string
  state: 'waiting' | 'live' | 'ended' | 'error'
  message: string
}

function createSuffix(): string {
  return Math.floor(Math.random() * 9000 + 1000).toString()
}

export function createTemporaryDisplayName(role: 'host' | 'viewer'): string {
  const prefix = role === 'host' ? 'Host' : 'Guest'
  return `${prefix} ${createSuffix()}`
}

export function formatChatTimestamp(sentAtUtc: string): string {
  return new Intl.DateTimeFormat(undefined, {
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(sentAtUtc))
}
