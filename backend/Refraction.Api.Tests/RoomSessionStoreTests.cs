using Refraction.Api.Models;
using Refraction.Api.Services;

namespace Refraction.Api.Tests;

public sealed class RoomSessionStoreTests
{
    [Fact]
    public void Create_GeneratesWaitingSession()
    {
        var store = new InMemoryRoomSessionStore();

        var session = store.Create();

        Assert.NotNull(session.RoomId);
        Assert.NotNull(session.RoomSlug);
        Assert.Equal(RoomState.Waiting, session.State);
    }

    [Fact]
    public void MarkLive_TransitionsWaitingSessionToLive()
    {
        var store = new InMemoryRoomSessionStore();
        var session = store.Create();

        var updated = store.MarkLive(session.RoomSlug);

        Assert.NotNull(updated);
        Assert.Equal(RoomState.Live, updated!.State);
        Assert.NotNull(updated.ActivatedAtUtc);
    }

    [Fact]
    public void MarkEnded_TransitionsLiveSessionToEnded()
    {
        var store = new InMemoryRoomSessionStore();
        var session = store.Create();
        store.MarkLive(session.RoomSlug);

        var updated = store.MarkEnded(session.RoomSlug);

        Assert.NotNull(updated);
        Assert.Equal(RoomState.Ended, updated!.State);
        Assert.NotNull(updated.EndedAtUtc);
    }
}
