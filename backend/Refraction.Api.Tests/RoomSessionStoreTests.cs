using Refraction.Api.Configuration;
using Refraction.Api.Models;
using Refraction.Api.Services;

namespace Refraction.Api.Tests;

public sealed class RoomSessionStoreTests
{
    [Fact]
    public void Create_GeneratesWaitingSession()
    {
        var store = CreateStore();

        var session = store.Create();

        Assert.NotNull(session.RoomId);
        Assert.NotNull(session.RoomSlug);
        Assert.Equal(RoomState.Waiting, session.State);
    }

    [Fact]
    public void MarkLive_TransitionsWaitingSessionToLive()
    {
        var store = CreateStore();
        var session = store.Create();

        var updated = store.MarkLive(session.RoomSlug);

        Assert.NotNull(updated);
        Assert.Equal(RoomState.Live, updated!.State);
        Assert.NotNull(updated.ActivatedAtUtc);
    }

    [Fact]
    public void MarkEnded_TransitionsLiveSessionToEnded()
    {
        var store = CreateStore();
        var session = store.Create();
        store.MarkLive(session.RoomSlug);

        var updated = store.MarkEnded(session.RoomSlug);

        Assert.NotNull(updated);
        Assert.Equal(RoomState.Ended, updated!.State);
        Assert.NotNull(updated.EndedAtUtc);
    }

    [Fact]
    public void TryUpdateState_ReturnsConflictForDuplicateLiveUpdate()
    {
        var store = CreateStore();
        var session = store.Create();
        store.MarkLive(session.RoomSlug);

        var result = store.TryUpdateState(session.RoomSlug, RoomState.Live);

        Assert.Equal(RoomStateUpdateStatus.InvalidTransition, result.Status);
        Assert.Equal("invalid_room_state_transition", result.Code);
        Assert.Equal(RoomState.Live, result.Session!.State);
    }

    [Fact]
    public void TryUpdateState_ReturnsConflictForEndedRoomGoingLive()
    {
        var store = CreateStore();
        var session = store.Create();
        store.MarkEnded(session.RoomSlug);

        var result = store.TryUpdateState(session.RoomSlug, RoomState.Live);

        Assert.Equal(RoomStateUpdateStatus.InvalidTransition, result.Status);
        Assert.Equal(RoomState.Ended, result.Session!.State);
    }

    [Fact]
    public void TryUpdateState_ReturnsBadRequestStatusForUnsupportedState()
    {
        var store = CreateStore();

        var result = store.TryUpdateState("missing", RoomState.Waiting);

        Assert.Equal(RoomStateUpdateStatus.UnsupportedState, result.Status);
        Assert.Equal("unsupported_room_state", result.Code);
    }

    [Fact]
    public void GetBySlug_RemovesWaitingSessionAfterWaitingTtl()
    {
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-03-21T00:00:00Z"));
        var store = CreateStore(timeProvider, new RoomSessionOptions
        {
            WaitingRoomTtl = TimeSpan.FromMinutes(5),
            LiveRoomTtl = TimeSpan.FromHours(1),
            EndedRoomRetention = TimeSpan.FromMinutes(10),
            CleanupInterval = TimeSpan.FromSeconds(30)
        });

        var session = store.Create();
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        var resolved = store.GetBySlug(session.RoomSlug);

        Assert.Null(resolved);
    }

    [Fact]
    public void TryUpdateState_TreatsExpiredLiveSessionAsEndedConflict()
    {
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-03-21T00:00:00Z"));
        var store = CreateStore(timeProvider, new RoomSessionOptions
        {
            WaitingRoomTtl = TimeSpan.FromMinutes(5),
            LiveRoomTtl = TimeSpan.FromMinutes(30),
            EndedRoomRetention = TimeSpan.FromMinutes(10),
            CleanupInterval = TimeSpan.FromSeconds(30)
        });

        var session = store.Create();
        store.MarkLive(session.RoomSlug);
        timeProvider.Advance(TimeSpan.FromMinutes(31));

        var result = store.TryUpdateState(session.RoomSlug, RoomState.Live);

        Assert.Equal(RoomStateUpdateStatus.InvalidTransition, result.Status);
        Assert.Equal(RoomState.Ended, result.Session!.State);
    }

    [Fact]
    public void GetBySlug_AutoEndsLiveSessionAfterLiveTtl()
    {
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-03-21T00:00:00Z"));
        var store = CreateStore(timeProvider, new RoomSessionOptions
        {
            WaitingRoomTtl = TimeSpan.FromMinutes(5),
            LiveRoomTtl = TimeSpan.FromMinutes(30),
            EndedRoomRetention = TimeSpan.FromMinutes(10),
            CleanupInterval = TimeSpan.FromSeconds(30)
        });

        var session = store.Create();
        store.MarkLive(session.RoomSlug);
        timeProvider.Advance(TimeSpan.FromMinutes(31));

        var resolved = store.GetBySlug(session.RoomSlug);

        Assert.NotNull(resolved);
        Assert.Equal(RoomState.Ended, resolved!.State);
        Assert.NotNull(resolved.EndedAtUtc);
    }

    [Fact]
    public void CleanupExpiredSessions_RemovesEndedSessionAfterRetentionWindow()
    {
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-03-21T00:00:00Z"));
        var store = CreateStore(timeProvider, new RoomSessionOptions
        {
            WaitingRoomTtl = TimeSpan.FromMinutes(5),
            LiveRoomTtl = TimeSpan.FromMinutes(30),
            EndedRoomRetention = TimeSpan.FromMinutes(10),
            CleanupInterval = TimeSpan.FromSeconds(30)
        });

        var session = store.Create();
        store.MarkEnded(session.RoomSlug);
        timeProvider.Advance(TimeSpan.FromMinutes(11));

        var removedCount = store.CleanupExpiredSessions();

        Assert.Equal(1, removedCount);
        Assert.Null(store.GetBySlug(session.RoomSlug));
    }

    private static InMemoryRoomSessionStore CreateStore(TimeProvider? timeProvider = null, RoomSessionOptions? options = null)
    {
        return new InMemoryRoomSessionStore(
            options ?? new RoomSessionOptions(),
            timeProvider ?? new AdjustableTimeProvider(DateTimeOffset.Parse("2026-03-21T00:00:00Z")));
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow;

        public AdjustableTimeProvider(DateTimeOffset utcNow)
        {
            this.utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan by)
        {
            utcNow = utcNow.Add(by);
        }
    }
}
