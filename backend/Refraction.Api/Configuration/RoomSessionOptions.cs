namespace Refraction.Api.Configuration;

public sealed class RoomSessionOptions
{
    public TimeSpan WaitingRoomTtl { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan LiveRoomTtl { get; init; } = TimeSpan.FromHours(4);
    public TimeSpan EndedRoomRetention { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan CleanupInterval { get; init; } = TimeSpan.FromSeconds(30);
}
