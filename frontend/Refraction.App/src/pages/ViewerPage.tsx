import { Room, RoomEvent, Track } from 'livekit-client'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { resolveRoom } from '../api/client'
import { StatusBadge } from '../components/StatusBadge'
import type { ResolveRoomResponse, ViewerState } from '../types/app'

const viewerLabels: Record<ViewerState, { tone: 'neutral' | 'success' | 'warning' | 'danger'; text: string }> = {
  loading: { tone: 'warning', text: 'Loading' },
  resolving_room: { tone: 'warning', text: 'Resolving room' },
  joining: { tone: 'warning', text: 'Joining' },
  waiting_for_host: { tone: 'neutral', text: 'Waiting for host' },
  live: { tone: 'success', text: 'Live' },
  ended: { tone: 'neutral', text: 'Ended' },
  disconnected: { tone: 'warning', text: 'Disconnected' },
  error: { tone: 'danger', text: 'Error' },
}

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unable to load the stream.'
}

export function ViewerPage() {
  const { slug } = useParams<{ slug: string }>()
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const roomRef = useRef<Room | null>(null)
  const [status, setStatus] = useState<ViewerState>('loading')
  const [message, setMessage] = useState<string>('Loading stream…')
  const [roomInfo, setRoomInfo] = useState<ResolveRoomResponse | null>(null)

  const badge = useMemo(() => viewerLabels[status], [status])

  useEffect(() => {
    if (!slug) {
      return
    }

    let disposed = false
    let pollId: number | undefined

    const disconnect = () => {
      roomRef.current?.disconnect()
      roomRef.current = null
      if (videoRef.current) {
        videoRef.current.srcObject = null
      }
    }

    const attachExistingTrack = () => {
      const room = roomRef.current
      if (!room) {
        return false
      }

      for (const participant of room.remoteParticipants.values()) {
        for (const publication of participant.trackPublications.values()) {
          if (publication.track?.kind === Track.Kind.Video && videoRef.current) {
            publication.track.attach(videoRef.current)
            return true
          }
        }
      }

      return false
    }

    const startPolling = () => {
      pollId = window.setInterval(async () => {
        try {
          const next = await resolveRoom(slug)
          if (disposed) {
            return
          }

          setRoomInfo(next)
          if (next.state === 'ended') {
            setStatus('ended')
            setMessage(next.message ?? 'This stream has ended.')
            disconnect()
          }
        } catch {
          if (!disposed) {
            setStatus('disconnected')
            setMessage('Lost contact with the backend while watching the stream.')
          }
        }
      }, 5000)
    }

    const boot = async () => {
      try {
        setStatus('resolving_room')
        const resolved = await resolveRoom(slug)
        if (disposed) {
          return
        }

        setRoomInfo(resolved)
        setMessage(resolved.message ?? 'Room resolved.')

        if (resolved.state === 'ended' || !resolved.viewerToken) {
          setStatus('ended')
          return
        }

        setStatus('joining')
        const room = new Room({
          adaptiveStream: true,
          dynacast: false,
        })
        roomRef.current = room

        room.on(RoomEvent.TrackSubscribed, (track) => {
          if (track.kind === Track.Kind.Video && videoRef.current) {
            track.attach(videoRef.current)
            setStatus('live')
            setMessage('Watching the host screen live.')
          }
        })

        room.on(RoomEvent.TrackUnsubscribed, () => {
          if (!disposed) {
            setStatus('waiting_for_host')
            setMessage('Waiting for the host to resume or republish their screen.')
          }
        })

        room.on(RoomEvent.Disconnected, () => {
          if (!disposed) {
            setStatus('disconnected')
            setMessage('Disconnected from the stream.')
          }
        })

        await room.connect(resolved.liveKitUrl, resolved.viewerToken)

        if (!attachExistingTrack()) {
          setStatus('waiting_for_host')
          setMessage('Connected. Waiting for the host screen to appear.')
        }

        startPolling()
      } catch (error) {
        if (!disposed) {
          setStatus('error')
          setMessage(getErrorMessage(error))
        }
      }
    }

    void boot()

    return () => {
      disposed = true
      if (pollId) {
        window.clearInterval(pollId)
      }
      disconnect()
    }
  }, [slug])

  if (!slug) {
    return (
      <main className="shell">
        <section className="panel hero-panel viewer-panel">
          <div className="hero-copy">
            <p className="eyebrow">Viewer</p>
            <h1>Invalid room</h1>
            <p className="lede">This viewer URL is missing a room slug.</p>
          </div>
          <div className="hero-status">
            <StatusBadge tone="danger" label="Error" />
            <p className="error-text">Missing room link.</p>
          </div>
        </section>
      </main>
    )
  }

  return (
    <main className="shell">
      <section className="panel hero-panel viewer-panel">
        <div className="hero-copy">
          <p className="eyebrow">Viewer</p>
          <h1>Watch stream</h1>
          <p className="lede">Passive viewer mode only. No upstream media, chat, or controls.</p>
        </div>
        <div className="hero-status">
          <StatusBadge tone={badge.tone} label={badge.text} />
          <p className={status === 'error' ? 'error-text' : 'muted-text'}>{message}</p>
        </div>
      </section>

      <section className="grid viewer-grid">
        <article className="panel panel--stack panel--full">
          <div className="panel-header">
            <h2>Live video</h2>
            <p>Viewer link: <code>/r/{slug}</code></p>
          </div>
          <div className="video-frame video-frame--large">
            <video ref={videoRef} className="video-element" autoPlay playsInline />
            {status !== 'live' ? <div className="video-empty">{message}</div> : null}
          </div>
        </article>

        <article className="panel panel--stack">
          <div className="panel-header">
            <h2>Session</h2>
            <p>What the viewer currently knows about the room.</p>
          </div>
          <div className="details-list">
            <div>
              <span>Room slug</span>
              <strong>{roomInfo?.roomSlug ?? slug}</strong>
            </div>
            <div>
              <span>Backend state</span>
              <strong>{roomInfo?.state ?? 'loading'}</strong>
            </div>
            <div>
              <span>Mode</span>
              <strong>Subscriber only</strong>
            </div>
          </div>
        </article>
      </section>
    </main>
  )
}
