using Refraction.Api.Configuration;

namespace Refraction.Api.Services;

public sealed class RoomSessionCleanupService : BackgroundService
{
    private readonly InMemoryRoomSessionStore store;
    private readonly RoomSessionOptions options;
    private readonly ILogger<RoomSessionCleanupService> logger;
    private readonly TimeProvider timeProvider;

    public RoomSessionCleanupService(
        InMemoryRoomSessionStore store,
        RoomSessionOptions options,
        ILogger<RoomSessionCleanupService> logger,
        TimeProvider timeProvider)
    {
        this.store = store;
        this.options = options;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.CleanupInterval, timeProvider);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removedCount = store.CleanupExpiredSessions();
            if (removedCount > 0)
            {
                logger.LogDebug("Cleaned up {RemovedCount} expired room sessions.", removedCount);
            }
        }
    }
}
