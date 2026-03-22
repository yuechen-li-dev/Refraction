using Refraction.Api.Services.Chat;

namespace Refraction.Api.Tests;

public sealed class InMemoryRoomChatStoreTests
{
    [Fact]
    public void AddUserMessage_StoresRecentMessage()
    {
        var store = new InMemoryRoomChatStore(TimeProvider.System);

        var message = store.AddUserMessage("room-one", "Guest 1234", "Hello chat");
        var recent = store.GetRecentMessages("room-one");

        Assert.Single(recent);
        Assert.Equal(message.Id, recent[0].Id);
        Assert.Equal("Guest 1234", recent[0].DisplayName);
        Assert.Equal("Hello chat", recent[0].Text);
    }

    [Fact]
    public void AddUserMessage_TrimsBufferToMostRecentFiftyMessages()
    {
        var store = new InMemoryRoomChatStore(TimeProvider.System);

        for (var index = 1; index <= 55; index++)
        {
            store.AddUserMessage("room-one", "Guest 1234", $"Message {index}");
        }

        var recent = store.GetRecentMessages("room-one");

        Assert.Equal(50, recent.Count);
        Assert.Equal("Message 6", recent[0].Text);
        Assert.Equal("Message 55", recent[^1].Text);
    }

    [Fact]
    public void CleanupInactiveRooms_RemovesBuffersForInactiveRooms()
    {
        var store = new InMemoryRoomChatStore(TimeProvider.System);
        store.AddUserMessage("active-room", "Guest 1111", "Still here");
        store.AddUserMessage("expired-room", "Guest 2222", "Gone soon");

        var removed = store.CleanupInactiveRooms(roomSlug => roomSlug == "active-room");

        Assert.Equal(1, removed);
        Assert.Single(store.GetRecentMessages("active-room"));
        Assert.Empty(store.GetRecentMessages("expired-room"));
    }
}
