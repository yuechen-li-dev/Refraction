using System.Collections.Concurrent;
using Refraction.Api.Models;

namespace Refraction.Api.Services;

public sealed class InMemoryRoomSessionStore : IRoomSessionStore
{
    private readonly ConcurrentDictionary<string, RoomSession> sessions = new(StringComparer.OrdinalIgnoreCase);

    public RoomSession Create()
    {
        while (true)
        {
            var roomId = $"room-{Guid.NewGuid():N}";
            var roomSlug = RoomSlugGenerator.Create();
            var session = new RoomSession(roomId, roomSlug, DateTimeOffset.UtcNow, null, null);

            if (sessions.TryAdd(roomSlug, session))
            {
                return session;
            }
        }
    }

    public RoomSession? GetBySlug(string roomSlug)
    {
        return sessions.TryGetValue(roomSlug, out var session) ? session : null;
    }

    public RoomSession? MarkLive(string roomSlug)
    {
        return Update(roomSlug, session => session with { ActivatedAtUtc = session.ActivatedAtUtc ?? DateTimeOffset.UtcNow });
    }

    public RoomSession? MarkEnded(string roomSlug)
    {
        return Update(roomSlug, session => session.EndedAtUtc is not null
            ? session
            : session with { EndedAtUtc = DateTimeOffset.UtcNow });
    }

    private RoomSession? Update(string roomSlug, Func<RoomSession, RoomSession> update)
    {
        while (sessions.TryGetValue(roomSlug, out var current))
        {
            var next = update(current);
            if (sessions.TryUpdate(roomSlug, next, current))
            {
                return next;
            }
        }

        return null;
    }
}
