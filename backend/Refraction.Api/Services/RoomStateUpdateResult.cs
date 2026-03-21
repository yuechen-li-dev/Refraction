using Refraction.Api.Models;

namespace Refraction.Api.Services;

public enum RoomStateUpdateStatus
{
    Updated,
    NotFound,
    InvalidTransition,
    UnsupportedState
}

public sealed record RoomStateUpdateResult(
    RoomStateUpdateStatus Status,
    RoomSession? Session,
    string Code,
    string Message);
