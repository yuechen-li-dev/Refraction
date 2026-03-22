import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'
import { useEffect, useMemo, useRef, useState } from 'react'
import { apiBaseUrl } from '../api/client'
import type { RoomState } from '../types/app'
import {
  createTemporaryDisplayName,
  formatChatTimestamp,
  type ChatClosedEvent,
  type ChatConnectionState,
  type ChatMessage,
} from '../lib/chat'

interface ChatPanelProps {
  roomSlug: string | null
  roomState: RoomState | null
  role: 'host' | 'viewer'
  title?: string
}

const connectionLabels: Record<ChatConnectionState, string> = {
  idle: 'Waiting for a valid room…',
  connecting: 'Connecting to room chat…',
  connected: 'Chat is live for this session.',
  error: 'Chat connection failed. Retrying can help once the room is active.',
  closed: 'Chat is closed for this room.',
}

function isChatEnabled(roomState: RoomState | null): boolean {
  return roomState === 'waiting' || roomState === 'live'
}

function getTerminalMessage(roomState: RoomState | null): string {
  if (roomState === 'ended') {
    return 'Chat ended with the stream.'
  }

  if (roomState === 'error') {
    return 'Chat is unavailable because the room is invalid.'
  }

  return 'Create or resolve a room to chat.'
}

export function ChatPanel({ roomSlug, roomState, role, title = 'Chat' }: ChatPanelProps) {
  const currentNameRef = useRef<string>('')
  const connectionRef = useRef<HubConnection | null>(null)
  const messagesEndRef = useRef<HTMLDivElement | null>(null)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [identity, setIdentity] = useState(() => {
    const name = createTemporaryDisplayName(role)
    return { displayName: name, draftName: name }
  })
  const [draftMessage, setDraftMessage] = useState('')
  const [connectionState, setConnectionState] = useState<ChatConnectionState>('idle')
  const [feedback, setFeedback] = useState<string>('')
  const [hasSentMessage, setHasSentMessage] = useState(false)

  const displayName = identity.displayName
  const draftName = identity.draftName
  const chatEnabled = isChatEnabled(roomState) && Boolean(roomSlug)

  useEffect(() => {
    currentNameRef.current = displayName
  }, [displayName])

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ block: 'end' })
  }, [messages])

  useEffect(() => {
    if (!chatEnabled || !roomSlug) {
      void connectionRef.current?.stop()
      connectionRef.current = null
      return
    }

    let cancelled = false
    let activeConnection: HubConnection | null = null

    const connection = new HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/chat`, {
        withCredentials: false,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build()

    activeConnection = connection
    connectionRef.current = connection

    connection.on('ReceiveRecentMessages', (nextMessages: ChatMessage[]) => {
      if (!cancelled) {
        setMessages(nextMessages)
      }
    })

    connection.on('ReceiveMessage', (message: ChatMessage) => {
      if (!cancelled) {
        setMessages((current) => [...current, message])
      }
    })

    connection.on('DisplayNameUpdated', (nextDisplayName: string) => {
      if (!cancelled) {
        currentNameRef.current = nextDisplayName
        setIdentity({ displayName: nextDisplayName, draftName: nextDisplayName })
      }
    })

    connection.on('ChatClosed', (event: ChatClosedEvent) => {
      if (!cancelled) {
        setConnectionState('closed')
        setFeedback(event.message)
      }
    })

    connection.onreconnecting(() => {
      if (!cancelled) {
        setConnectionState('connecting')
        setFeedback('Reconnecting to chat…')
      }
      return Promise.resolve()
    })

    connection.onreconnected(async () => {
      if (cancelled) {
        return
      }

      try {
        await connection.invoke('JoinRoom', { roomSlug, displayName: currentNameRef.current })
        setConnectionState('connected')
        setFeedback('')
      } catch (error) {
        setConnectionState('error')
        setFeedback(error instanceof Error ? error.message : 'Unable to rejoin chat.')
      }
    })

    connection.onclose((error) => {
      if (cancelled) {
        return Promise.resolve()
      }

      setConnectionState(chatEnabled ? 'error' : 'closed')
      if (error) {
        setFeedback(error.message)
      }

      return Promise.resolve()
    })

    const start = async () => {
      try {
        setConnectionState('connecting')
        setFeedback('')
        await connection.start()
        if (cancelled) {
          await connection.stop()
          return
        }

        await connection.invoke('JoinRoom', { roomSlug, displayName: currentNameRef.current })
        setConnectionState('connected')
      } catch (error) {
        if (cancelled) {
          return
        }

        setConnectionState('error')
        setFeedback(error instanceof Error ? error.message : 'Unable to connect to chat.')
      }
    }

    void start()

    return () => {
      cancelled = true
      if (activeConnection) {
        void activeConnection.stop()
      }
      if (connectionRef.current === activeConnection) {
        connectionRef.current = null
      }
    }
  }, [chatEnabled, roomSlug, roomState])

  const canEditName = !hasSentMessage && chatEnabled && connectionState !== 'closed'

  const panelStatus = useMemo(() => {
    if (!chatEnabled) {
      return getTerminalMessage(roomState)
    }

    return connectionLabels[connectionState]
  }, [chatEnabled, connectionState, roomState])

  const handleSaveName = async () => {
    const nextName = draftName.trim()
    if (!nextName) {
      setFeedback('Enter a temporary display name before chatting.')
      return
    }

    currentNameRef.current = nextName

    const connection = connectionRef.current
    if (!connection || connection.state !== HubConnectionState.Connected || !roomSlug) {
      setIdentity({ displayName: nextName, draftName: nextName })
      setFeedback('Name saved locally. It will apply once chat reconnects.')
      return
    }

    try {
      await connection.invoke('UpdateDisplayName', { roomSlug, displayName: nextName })
      setFeedback('Temporary name updated.')
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : 'Unable to update display name.')
    }
  }

  const handleSendMessage = async () => {
    const text = draftMessage.trim()
    if (!text) {
      return
    }

    const connection = connectionRef.current
    if (!connection || connection.state !== HubConnectionState.Connected) {
      setFeedback('Chat is not connected right now.')
      return
    }

    try {
      if (draftName.trim() && draftName.trim() !== displayName && roomSlug) {
        currentNameRef.current = draftName.trim()
        await connection.invoke('UpdateDisplayName', { roomSlug, displayName: draftName.trim() })
      }

      await connection.invoke('SendMessage', { text })
      setDraftMessage('')
      setHasSentMessage(true)
      setFeedback('')
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : 'Unable to send message.')
    }
  }

  return (
    <article className="panel panel--stack panel--chat">
      <div className="panel-header">
        <h2>{title}</h2>
        <p>{panelStatus}</p>
      </div>

      <div className="chat-name-row">
        <label className="chat-field">
          <span>Temporary name</span>
          <input
            value={draftName}
            onChange={(event) => setIdentity((current) => ({ ...current, draftName: event.target.value }))}
            disabled={!canEditName}
            maxLength={40}
            placeholder="Guest 1234"
          />
        </label>
        <button className="button button--secondary" onClick={() => void handleSaveName()} disabled={!canEditName}>
          Save
        </button>
      </div>
      <p className="muted-text chat-helper">
        Session-scoped only. Your name disappears when this room ends.
      </p>

      <div className="chat-feed" aria-live="polite">
        {messages.length === 0 ? (
          <div className="chat-empty">
            {chatEnabled ? 'No chat messages yet for this session.' : getTerminalMessage(roomState)}
          </div>
        ) : (
          messages.map((message) => (
            <div key={message.id} className={`chat-message chat-message--${message.kind}`}>
              <div className="chat-message__meta">
                <strong>{message.kind === 'system' ? 'System' : message.displayName}</strong>
                <span>{formatChatTimestamp(message.sentAtUtc)}</span>
              </div>
              <p>{message.text}</p>
            </div>
          ))
        )}
        <div ref={messagesEndRef} />
      </div>

      <div className="chat-compose">
        <textarea
          value={draftMessage}
          onChange={(event) => setDraftMessage(event.target.value)}
          rows={3}
          maxLength={500}
          placeholder={chatEnabled ? 'Send a message to everyone in this room…' : getTerminalMessage(roomState)}
          disabled={!chatEnabled || connectionState !== 'connected'}
          onKeyDown={(event) => {
            if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
              event.preventDefault()
              void handleSendMessage()
            }
          }}
        />
        <div className="chat-compose__footer">
          <span className="muted-text">
            {feedback || 'Press Ctrl/Cmd + Enter to send.'}
          </span>
          <button
            className="button button--primary"
            onClick={() => void handleSendMessage()}
            disabled={!chatEnabled || connectionState !== 'connected' || !draftMessage.trim()}
          >
            Send
          </button>
        </div>
      </div>
    </article>
  )
}
