import { Room, RoomEvent, Track } from 'livekit-client'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { ApiRequestError, resolveRoom } from '../api/client'
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

const activePollIntervalMs = 4000
const disconnectedRetryIntervalMs = 3000
const fastStateCheckDelayMs = 250

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Unable to load the stream.'
}

function isEndedState(roomInfo: ResolveRoomResponse): boolean {
  return roomInfo.state === 'ended' || !roomInfo.viewerToken
}

function isDeadRoomError(error: unknown): error is ApiRequestError {
  return error instanceof ApiRequestError && error.code === 'room_not_found'
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

    const clearPoll = () => {
      if (pollId) {
        window.clearTimeout(pollId)
        pollId = undefined
      }
    }

    const clearVideo = () => {
      if (videoRef.current) {
        videoRef.current.srcObject = null
      }
    }

    const disconnectRoom = () => {
      const room = roomRef.current
      roomRef.current = null
      room?.disconnect()
      clearVideo()
    }

    const transitionToDeadRoom = (error: ApiRequestError) => {
      disconnectRoom()
      setStatus('ended')
      setMessage(error.message || 'This stream is no longer active.')
    }

    const schedulePoll = (delayMs: number) => {
      if (disposed) {
        return
      }

      clearPoll()
      pollId = window.setTimeout(() => {
        void pollRoomState()
      }, delayMs)
    }

    const attachExistingTrack = (room: Room) => {
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

    const resolveLatestRoom = async () => {
      const next = await resolveRoom(slug)
      if (disposed) {
        return null
      }

      setRoomInfo(next)
      return next
    }

    const transitionToEnded = (next: ResolveRoomResponse) => {
      disconnectRoom()
      setRoomInfo(next)
      setStatus('ended')
      setMessage(next.message ?? 'This stream has ended.')
    }

    const reconcileAfterConnectFailure = async (connectError: unknown) => {
      try {
        const latest = await resolveLatestRoom()
        if (!latest) {
          return true
        }

        if (isEndedState(latest)) {
          transitionToEnded(latest)
          return true
        }
      } catch (resolveError) {
        if (isDeadRoomError(resolveError)) {
          transitionToDeadRoom(resolveError)
          return true
        }
      }

      throw connectError
    }

    const connectToRoom = async (next: ResolveRoomResponse, reconnecting: boolean) => {
      if (isEndedState(next)) {
        transitionToEnded(next)
        return
      }

      clearPoll()
      disconnectRoom()

      setStatus('joining')
      setMessage(reconnecting ? 'Rejoining the stream…' : 'Joining the stream…')

      const room = new Room({
        adaptiveStream: true,
        dynacast: false,
      })

      roomRef.current = room

      room.on(RoomEvent.TrackSubscribed, (track) => {
        if (disposed || roomRef.current !== room) {
          return
        }

        if (track.kind === Track.Kind.Video && videoRef.current) {
          track.attach(videoRef.current)
          setStatus('live')
          setMessage('Watching the host screen live.')
        }
      })

      room.on(RoomEvent.TrackUnsubscribed, () => {
        if (disposed || roomRef.current !== room) {
          return
        }

        clearVideo()
        setStatus('waiting_for_host')
        setMessage('Waiting for the host to resume or republish their screen.')
        schedulePoll(fastStateCheckDelayMs)
      })

      room.on(RoomEvent.Disconnected, () => {
        if (disposed || roomRef.current !== room) {
          return
        }

        roomRef.current = null
        clearVideo()
        setStatus('resolving_room')
        setMessage('Stream connection dropped. Rechecking the room…')
        schedulePoll(fastStateCheckDelayMs)
      })

      const viewerToken = next.viewerToken
      if (!viewerToken) {
        transitionToEnded(next)
        return
      }

      try {
        await room.connect(next.liveKitUrl, viewerToken)
      } catch (error) {
        if (disposed || roomRef.current !== room) {
          room.disconnect()
          return
        }

        disconnectRoom()
        await reconcileAfterConnectFailure(error)
        return
      }

      if (disposed || roomRef.current !== room) {
        room.disconnect()
        return
      }

      if (!attachExistingTrack(room)) {
        setStatus('waiting_for_host')
        setMessage(next.state === 'live'
          ? 'Connected. Waiting for the host screen to appear.'
          : next.message ?? 'Waiting for the host to start streaming.')
      }

      schedulePoll(activePollIntervalMs)
    }

    const pollRoomState = async () => {
      try {
        const next = await resolveLatestRoom()
        if (!next) {
          return
        }

        if (isEndedState(next)) {
          transitionToEnded(next)
          return
        }

        if (!roomRef.current) {
          await connectToRoom(next, true)
          return
        }

        schedulePoll(activePollIntervalMs)
      } catch (error) {
        if (isDeadRoomError(error)) {
          transitionToDeadRoom(error)
          return
        }

        if (!disposed) {
          setStatus('disconnected')
          setMessage('Lost contact with the backend. Retrying while the session is still active.')
          schedulePoll(disconnectedRetryIntervalMs)
        }
      }
    }

    const boot = async () => {
      try {
        setStatus('resolving_room')
        setMessage('Resolving room…')

        const next = await resolveLatestRoom()
        if (!next) {
          return
        }

        setMessage(next.message ?? 'Room resolved.')

        if (isEndedState(next)) {
          transitionToEnded(next)
          return
        }

        await connectToRoom(next, false)
      } catch (error) {
        if (isDeadRoomError(error)) {
          transitionToDeadRoom(error)
          return
        }

        if (!disposed) {
          setStatus('error')
          setMessage(getErrorMessage(error))
        }
      }
    }

    void boot()

    return () => {
      disposed = true
      clearPoll()
      disconnectRoom()
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
