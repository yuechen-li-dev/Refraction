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

    public bool IsWaitingExpired(DateTimeOffset now, TimeSpan waitingRoomTtl) =>
        ActivatedAtUtc is null && EndedAtUtc is null && now - CreatedAtUtc >= waitingRoomTtl;

    public bool IsLiveExpired(DateTimeOffset now, TimeSpan liveRoomTtl) =>
        ActivatedAtUtc is not null && EndedAtUtc is null && now - ActivatedAtUtc.Value >= liveRoomTtl;

    public bool IsEndedRetentionExpired(DateTimeOffset now, TimeSpan endedRoomRetention) =>
        EndedAtUtc is not null && now - EndedAtUtc.Value >= endedRoomRetention;
}
