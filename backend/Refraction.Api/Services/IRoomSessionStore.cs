using Refraction.Api.Models;

namespace Refraction.Api.Services;

public interface IRoomSessionStore
{
    RoomSession Create();
    RoomSession? GetBySlug(string roomSlug);
    RoomSession? MarkLive(string roomSlug);
    RoomSession? MarkEnded(string roomSlug);
    RoomStateUpdateResult TryUpdateState(string roomSlug, RoomState state);
    int CleanupExpiredSessions();
}
