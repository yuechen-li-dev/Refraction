using Livekit.Server.Sdk.Dotnet;
using Refraction.Api.Configuration;
using Refraction.Api.Models;

namespace Refraction.Api.Services;

public sealed class LiveKitTokenService : ILiveKitTokenService
{
    private const int MaxRoomParticipants = 128;
    private readonly LiveKitOptions options;
    private readonly RoomServiceClient roomServiceClient;
    private readonly ILogger<LiveKitTokenService> logger;

    public LiveKitTokenService(LiveKitOptions options, ILogger<LiveKitTokenService> logger)
    {
        this.options = options;
        this.logger = logger;
        roomServiceClient = new RoomServiceClient(options.Url, options.ApiKey, options.ApiSecret);
    }

    public string CreateHostToken(RoomSession session)
    {
        return new AccessToken(options.ApiKey, options.ApiSecret)
            .WithIdentity($"host-{session.RoomSlug}")
            .WithName("Host")
            .WithTtl(TimeSpan.FromHours(2))
            .WithGrants(new VideoGrants
            {
                Room = session.RoomId,
                RoomJoin = true,
                CanPublish = true,
                CanSubscribe = false,
                CanPublishData = false
            })
            .ToJwt();
    }

    public string CreateViewerToken(RoomSession session)
    {
        return new AccessToken(options.ApiKey, options.ApiSecret)
            .WithIdentity($"viewer-{Guid.NewGuid():N}")
            .WithName("Viewer")
            .WithTtl(TimeSpan.FromHours(2))
            .WithGrants(new VideoGrants
            {
                Room = session.RoomId,
                RoomJoin = true,
                CanPublish = false,
                CanSubscribe = true,
                CanPublishData = false
            })
            .ToJwt();
    }

    public async Task EnsureRoomExistsAsync(RoomSession session, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await roomServiceClient.CreateRoom(new CreateRoomRequest
            {
                Name = session.RoomId,
                EmptyTimeout = 60 * 15,
                DepartureTimeout = 15,
                MaxParticipants = MaxRoomParticipants
            });
        }
        catch (Exception exception) when (exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(exception, "LiveKit room {RoomId} already existed.", session.RoomId);
        }
    }
}
