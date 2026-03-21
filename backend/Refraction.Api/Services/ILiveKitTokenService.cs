using Refraction.Api.Models;

namespace Refraction.Api.Services;

public interface ILiveKitTokenService
{
    string CreateHostToken(RoomSession session);
    string CreateViewerToken(RoomSession session);
    Task EnsureRoomExistsAsync(RoomSession session, CancellationToken cancellationToken);
}
