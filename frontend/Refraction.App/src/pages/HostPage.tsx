import {
  type LocalTrackPublication,
  Room,
  RoomEvent,
  Track,
} from 'livekit-client'
import { useCallback, useEffect, useRef, useState } from 'react'
import { apiBaseUrl, createRoom, endRoom, updateRoomState } from '../api/client'
import { StatusBadge } from '../components/StatusBadge'
import type { CreateRoomResponse, HostState } from '../types/app'

const hostLabels: Record<HostState, { tone: 'neutral' | 'success' | 'warning' | 'danger'; text: string }> = {
  idle: { tone: 'neutral', text: 'Idle' },
  requesting_capture: { tone: 'warning', text: 'Requesting capture' },
  creating_room: { tone: 'warning', text: 'Creating room' },
  connecting: { tone: 'warning', text: 'Connecting to LiveKit' },
  live: { tone: 'success', text: 'Live' },
  stopping: { tone: 'warning', text: 'Stopping' },
  ended: { tone: 'neutral', text: 'Ended' },
  error: { tone: 'danger', text: 'Error' },
}

function getMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong.'
}

export function HostPage() {
  const previewRef = useRef<HTMLVideoElement | null>(null)
  const roomRef = useRef<Room | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const publicationRef = useRef<LocalTrackPublication | null>(null)
  const isStoppingRef = useRef(false)
  const sessionRef = useRef<CreateRoomResponse | null>(null)

  const [status, setStatus] = useState<HostState>('idle')
  const [error, setError] = useState<string | null>(null)
  const [session, setSession] = useState<CreateRoomResponse | null>(null)
  const [copyFeedback, setCopyFeedback] = useState<string>('')
  const [hasPreview, setHasPreview] = useState(false)

  useEffect(() => {
    sessionRef.current = session
  }, [session])

  useEffect(() => {
    const notifyEndedOnPageExit = () => {
      if (isStoppingRef.current || !sessionRef.current) {
        return
      }

      const url = `${apiBaseUrl}/api/rooms/${sessionRef.current.roomSlug}/end`
      const requestBody = new Blob([], { type: 'application/json' })

      if (navigator.sendBeacon?.(url, requestBody)) {
        return
      }

      void fetch(url, {
        method: 'POST',
        keepalive: true,
      }).catch(() => undefined)
    }

    window.addEventListener('pagehide', notifyEndedOnPageExit)
    window.addEventListener('beforeunload', notifyEndedOnPageExit)

    return () => {
      window.removeEventListener('pagehide', notifyEndedOnPageExit)
      window.removeEventListener('beforeunload', notifyEndedOnPageExit)
    }
  }, [])

  const teardown = useCallback(async (notifyBackend: boolean) => {
    isStoppingRef.current = true

    publicationRef.current?.track?.stop()
    publicationRef.current = null

    const room = roomRef.current
    roomRef.current = null
    room?.disconnect()

    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
    setHasPreview(false)

    if (previewRef.current) {
      previewRef.current.srcObject = null
    }

    if (notifyBackend && sessionRef.current) {
      try {
        await endRoom(sessionRef.current.roomSlug)
      } catch {
        // best effort; the UI already transitions locally
      }
    }

    isStoppingRef.current = false
  }, [])

  const stopSharing = useCallback(async (reason?: string) => {
    if (!sessionRef.current && !streamRef.current && !roomRef.current) {
      setStatus('ended')
      if (reason) {
        setError(reason)
      }
      return
    }

    setStatus('stopping')
    setError(reason ?? null)

    await teardown(true)
    setStatus('ended')
  }, [teardown])

  useEffect(() => {
    return () => {
      void teardown(false)
    }
  }, [teardown])

  const handleShare = async () => {
    setError(null)
    setCopyFeedback('')
    setSession(null)

    try {
      setStatus('requesting_capture')
      const stream = await navigator.mediaDevices.getDisplayMedia({
        video: {
          displaySurface: 'monitor',
          frameRate: { ideal: 15, max: 30 },
        },
        audio: false,
      })

      streamRef.current = stream
      setHasPreview(true)

      const videoTrack = stream.getVideoTracks()[0]
      videoTrack.onended = () => {
        if (!isStoppingRef.current) {
          void stopSharing('Screen sharing stopped.')
        }
      }

      if (previewRef.current) {
        previewRef.current.srcObject = stream
        void previewRef.current.play().catch(() => undefined)
      }

      setStatus('creating_room')
      const roomSession = await createRoom()
      setSession(roomSession)

      setStatus('connecting')
      const room = new Room({
        adaptiveStream: false,
        dynacast: false,
      })

      roomRef.current = room

      room.on(RoomEvent.Disconnected, () => {
        if (!isStoppingRef.current && roomRef.current === room) {
          void stopSharing('Streaming session disconnected. The room has been closed.')
        }
      })

      await room.connect(roomSession.liveKitUrl, roomSession.hostToken)

      const publication = await room.localParticipant.publishTrack(videoTrack, {
        name: 'screen',
        source: Track.Source.ScreenShare,
      })

      publicationRef.current = publication
      await updateRoomState(roomSession.roomSlug, 'live')
      setStatus('live')
    } catch (captureError) {
      await teardown(Boolean(sessionRef.current))
      setStatus('error')
      setError(
        captureError instanceof DOMException && captureError.name === 'NotAllowedError'
          ? 'Screen sharing was canceled or blocked. Nothing is stuck — you can try again.'
          : getMessage(captureError),
      )
    }
  }

  const handleCopy = async () => {
    if (!session) {
      return
    }

    try {
      await navigator.clipboard.writeText(session.viewerUrl)
      setCopyFeedback('Copied link.')
    } catch {
      setCopyFeedback('Copy failed. Copy the URL manually.')
    }
  }

  const currentStatus = hostLabels[status]

  return (
    <main className="shell">
      <section className="panel hero-panel">
        <div className="hero-copy">
          <p className="eyebrow">M1 · one host, many passive viewers</p>
          <h1>Refraction</h1>
          <p className="lede">
            Share your screen, get a link, and let viewers watch. No chat, no audio, no extra chrome.
          </p>
          <div className="hero-actions">
            <button
              className="button button--primary"
              onClick={() => void handleShare()}
              disabled={status === 'requesting_capture' || status === 'creating_room' || status === 'connecting' || status === 'live' || status === 'stopping'}
            >
              Share screen
            </button>
            <button
              className="button button--secondary"
              onClick={() => void stopSharing('Screen sharing stopped.')}
              disabled={status !== 'live' && status !== 'error' && status !== 'ended'}
            >
              Stop
            </button>
          </div>
        </div>
        <div className="hero-status">
          <StatusBadge tone={currentStatus.tone} label={currentStatus.text} />
          {error ? <p className="error-text">{error}</p> : null}
        </div>
      </section>

      <section className="grid">
        <article className="panel panel--stack">
          <div className="panel-header">
            <h2>Preview</h2>
            <p>Your local display capture appears here.</p>
          </div>
          <div className="video-frame">
            <video ref={previewRef} className="video-element" autoPlay muted playsInline />
            {!hasPreview ? <div className="video-empty">Nothing captured yet.</div> : null}
          </div>
        </article>

        <article className="panel panel--stack">
          <div className="panel-header">
            <h2>Viewer link</h2>
            <p>Open this in one or more desktop Chromium windows or browser profiles.</p>
          </div>

          <div className="link-card">
            <code>{session?.viewerUrl ?? 'Link appears after capture starts.'}</code>
            <div className="hero-actions">
              <button className="button button--secondary" onClick={() => void handleCopy()} disabled={!session}>
                Copy link
              </button>
            </div>
            {copyFeedback ? <p className="muted-text">{copyFeedback}</p> : null}
          </div>

          <div className="details-list">
            <div>
              <span>Room slug</span>
              <strong>{session?.roomSlug ?? '—'}</strong>
            </div>
            <div>
              <span>Transport</span>
              <strong>LiveKit video only</strong>
            </div>
            <div>
              <span>Scope</span>
              <strong>1 host → passive viewers</strong>
            </div>
          </div>
        </article>
      </section>
    </main>
  )
}
