using System.Collections.Concurrent;
using Refraction.Api.Configuration;
using Refraction.Api.Models;

namespace Refraction.Api.Services;

public sealed class InMemoryRoomSessionStore : IRoomSessionStore
{
    private readonly ConcurrentDictionary<string, RoomSession> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly RoomSessionOptions options;
    private readonly TimeProvider timeProvider;

    public InMemoryRoomSessionStore(RoomSessionOptions options, TimeProvider timeProvider)
    {
        this.options = options;
        this.timeProvider = timeProvider;
    }

    public RoomSession Create()
    {
        CleanupExpiredSessions();

        while (true)
        {
            var roomId = $"room-{Guid.NewGuid():N}";
            var roomSlug = RoomSlugGenerator.Create();
            var session = new RoomSession(roomId, roomSlug, UtcNow, null, null);

            if (sessions.TryAdd(roomSlug, session))
            {
                return session;
            }
        }
    }

    public RoomSession? GetBySlug(string roomSlug)
    {
        return TransitionSession(roomSlug, allowAutoEnd: true);
    }

    public RoomSession? MarkLive(string roomSlug)
    {
        return Update(roomSlug, session => session.State == RoomState.Ended
            ? session
            : session with { ActivatedAtUtc = session.ActivatedAtUtc ?? UtcNow });
    }

    public RoomSession? MarkEnded(string roomSlug)
    {
        return Update(roomSlug, session => session.EndedAtUtc is not null
            ? session
            : session with { EndedAtUtc = UtcNow });
    }

    public RoomStateUpdateResult TryUpdateState(string roomSlug, RoomState state)
    {
        return state switch
        {
            RoomState.Live => TryTransition(roomSlug, transitionToLive: true),
            RoomState.Ended => TryTransition(roomSlug, transitionToLive: false),
            _ => new RoomStateUpdateResult(
                RoomStateUpdateStatus.UnsupportedState,
                null,
                "unsupported_room_state",
                "Only 'live' and 'ended' state updates are supported.")
        };
    }

    public int CleanupExpiredSessions()
    {
        var removedCount = 0;

        foreach (var roomSlug in sessions.Keys)
        {
            if (TransitionSession(roomSlug, allowAutoEnd: true) is null)
            {
                removedCount++;
            }
        }

        return removedCount;
    }

    private DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    private RoomSession? TransitionSession(string roomSlug, bool allowAutoEnd)
    {
        while (sessions.TryGetValue(roomSlug, out var current))
        {
            var next = Normalize(current, allowAutoEnd);
            if (ReferenceEquals(next, current))
            {
                return current;
            }

            if (next is null)
            {
                if (sessions.TryRemove(new KeyValuePair<string, RoomSession>(roomSlug, current)))
                {
                    return null;
                }

                continue;
            }

            if (sessions.TryUpdate(roomSlug, next, current))
            {
                return next;
            }
        }

        return null;
    }

    private RoomSession? Update(string roomSlug, Func<RoomSession, RoomSession> update)
    {
        while (true)
        {
            var current = TransitionSession(roomSlug, allowAutoEnd: false);
            if (current is null)
            {
                return null;
            }

            var next = Normalize(update(current), allowAutoEnd: false);
            if (next is null)
            {
                if (sessions.TryRemove(new KeyValuePair<string, RoomSession>(roomSlug, current)))
                {
                    return null;
                }

                continue;
            }

            if (sessions.TryUpdate(roomSlug, next, current))
            {
                return next;
            }
        }
    }

    private RoomStateUpdateResult TryTransition(string roomSlug, bool transitionToLive)
    {
        while (true)
        {
            var current = TransitionSession(roomSlug, allowAutoEnd: true);
            if (current is null)
            {
                return new RoomStateUpdateResult(
                    RoomStateUpdateStatus.NotFound,
                    null,
                    "room_not_found",
                    "Room link is invalid or has expired.");
            }

            if (transitionToLive)
            {
                if (current.State == RoomState.Live)
                {
                    return new RoomStateUpdateResult(
                        RoomStateUpdateStatus.InvalidTransition,
                        current,
                        "invalid_room_state_transition",
                        "Room is already marked live.");
                }

                if (current.State == RoomState.Ended)
                {
                    return new RoomStateUpdateResult(
                        RoomStateUpdateStatus.InvalidTransition,
                        current,
                        "invalid_room_state_transition",
                        "Ended rooms cannot transition back to live.");
                }

                var next = current with { ActivatedAtUtc = current.ActivatedAtUtc ?? UtcNow };
                if (sessions.TryUpdate(roomSlug, next, current))
                {
                    return new RoomStateUpdateResult(
                        RoomStateUpdateStatus.Updated,
                        next,
                        string.Empty,
                        "Host marked the stream live.");
                }

                continue;
            }

            if (current.State == RoomState.Ended)
            {
                return new RoomStateUpdateResult(
                    RoomStateUpdateStatus.InvalidTransition,
                    current,
                    "invalid_room_state_transition",
                    "Room is already ended.");
            }

            var ended = current with { EndedAtUtc = UtcNow };
            if (sessions.TryUpdate(roomSlug, ended, current))
            {
                return new RoomStateUpdateResult(
                    RoomStateUpdateStatus.Updated,
                    ended,
                    string.Empty,
                    "Stream state updated.");
            }
        }
    }

    private RoomSession? Normalize(RoomSession session, bool allowAutoEnd)
    {
        var now = UtcNow;

        if (session.IsEndedRetentionExpired(now, options.EndedRoomRetention))
        {
            return null;
        }

        if (session.IsWaitingExpired(now, options.WaitingRoomTtl))
        {
            return null;
        }

        if (allowAutoEnd && session.IsLiveExpired(now, options.LiveRoomTtl))
        {
            return session with { EndedAtUtc = now };
        }

        return session;
    }
}
