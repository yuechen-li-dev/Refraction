using Refraction.Api.Models;

namespace Refraction.Api.Contracts;

public sealed record CreateRoomRequest;

public sealed record CreateRoomResponse(
    string RoomId,
    string RoomSlug,
    string HostToken,
    string ViewerUrl,
    string LiveKitUrl,
    RoomState State);

public sealed record ResolveRoomResponse(
    string RoomId,
    string RoomSlug,
    string? ViewerToken,
    string LiveKitUrl,
    RoomState State,
    string? Message);

public sealed record UpdateRoomStateRequest(RoomState State);

public sealed record RoomEndedResponse(string RoomSlug, RoomState State);

public sealed record ErrorResponse(string Code, string Message, RoomState State);
