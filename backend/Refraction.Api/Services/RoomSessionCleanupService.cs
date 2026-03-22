using Refraction.Api.Configuration;
using Refraction.Api.Services.Chat;

namespace Refraction.Api.Services;

public sealed class RoomSessionCleanupService : BackgroundService
{
    private readonly InMemoryRoomSessionStore store;
    private readonly IRoomChatStore chatStore;
    private readonly RoomSessionOptions options;
    private readonly ILogger<RoomSessionCleanupService> logger;
    private readonly TimeProvider timeProvider;

    public RoomSessionCleanupService(
        InMemoryRoomSessionStore store,
        IRoomChatStore chatStore,
        RoomSessionOptions options,
        ILogger<RoomSessionCleanupService> logger,
        TimeProvider timeProvider)
    {
        this.store = store;
        this.chatStore = chatStore;
        this.options = options;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CleanupInterval, timeProvider);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removedSessions = store.CleanupExpiredSessions();
            var removedChats = chatStore.CleanupInactiveRooms(roomSlug => store.GetBySlug(roomSlug) is not null);

            if (removedSessions > 0 || removedChats > 0)
            {
                logger.LogDebug(
                    "Cleaned up {RemovedSessions} expired room sessions and {RemovedChats} inactive chat buffers.",
                    removedSessions,
                    removedChats);
            }
        }
    }
}
