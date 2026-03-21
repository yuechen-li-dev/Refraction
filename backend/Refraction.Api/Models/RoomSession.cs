namespace Refraction.Api.Models;

public sealed record RoomSession(
    string RoomId,
    string RoomSlug,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? EndedAtUtc)
{
    public RoomState State => EndedAtUtc is not null
        ? RoomState.Ended
        : ActivatedAtUtc is not null
            ? RoomState.Live
            : RoomState.Waiting;
}
