using Microsoft.AspNetCore.SignalR;
using Refraction.Api.Contracts;
using Refraction.Api.Models;
using Refraction.Api.Services;
using Refraction.Api.Services.Chat;

namespace Refraction.Api.Hubs;

public sealed class ChatHub : Hub<IChatClient>
{
    private const int MaxDisplayNameLength = 40;
    private const int MaxMessageLength = 500;
    private readonly IRoomSessionStore roomSessionStore;
    private readonly IRoomChatStore roomChatStore;
    private readonly IChatConnectionStore connectionStore;

    public ChatHub(
        IRoomSessionStore roomSessionStore,
        IRoomChatStore roomChatStore,
        IChatConnectionStore connectionStore)
    {
        this.roomSessionStore = roomSessionStore;
        this.roomChatStore = roomChatStore;
        this.connectionStore = connectionStore;
    }

    public async Task JoinRoom(JoinChatRequest request)
    {
        var roomSlug = NormalizeRoomSlug(request.RoomSlug);
        var displayName = NormalizeDisplayName(request.DisplayName);
        EnsureChatRoomIsActive(roomSlug);

        var previous = connectionStore.Get(Context.ConnectionId);
        if (previous is not null && !string.Equals(previous.RoomSlug, roomSlug, StringComparison.OrdinalIgnoreCase))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, previous.RoomSlug);
        }

        connectionStore.Set(Context.ConnectionId, new ChatConnectionInfo(roomSlug, displayName));
        await Groups.AddToGroupAsync(Context.ConnectionId, roomSlug);

        await Clients.Caller.ReceiveRecentMessages(roomChatStore
            .GetRecentMessages(roomSlug)
            .Select(ChatContracts.Map));
    }

    public async Task UpdateDisplayName(UpdateChatDisplayNameRequest request)
    {
        var roomSlug = NormalizeRoomSlug(request.RoomSlug);
        var displayName = NormalizeDisplayName(request.DisplayName);
        EnsureChatRoomIsActive(roomSlug);

        var current = connectionStore.Get(Context.ConnectionId);
        if (current is null || !string.Equals(current.RoomSlug, roomSlug, StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException("Join the room before changing your display name.");
        }

        connectionStore.UpdateDisplayName(Context.ConnectionId, displayName);
        await Clients.Caller.DisplayNameUpdated(displayName);
    }

    public async Task SendMessage(SendChatMessageRequest request)
    {
        var text = NormalizeMessageText(request.Text);
        var connection = connectionStore.Get(Context.ConnectionId)
            ?? throw new HubException("Join the room before sending a message.");

        EnsureChatRoomIsActive(connection.RoomSlug);

        var message = roomChatStore.AddUserMessage(connection.RoomSlug, connection.DisplayName, text);
        await Clients.Group(connection.RoomSlug).ReceiveMessage(ChatContracts.Map(message));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connectionStore.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private void EnsureChatRoomIsActive(string roomSlug)
    {
        var session = roomSessionStore.GetBySlug(roomSlug);
        if (session is null)
        {
            throw new HubException("Room link is invalid or has expired.");
        }

        if (session.State == RoomState.Ended)
        {
            throw new HubException("Chat is closed because this stream has ended.");
        }
    }

    private static string NormalizeRoomSlug(string roomSlug)
    {
        if (string.IsNullOrWhiteSpace(roomSlug))
        {
            throw new HubException("Room slug is required.");
        }

        return roomSlug.Trim();
    }

    private static string NormalizeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new HubException("Display name is required.");
        }

        var trimmed = displayName.Trim();
        if (trimmed.Length > MaxDisplayNameLength)
        {
            throw new HubException($"Display name must be {MaxDisplayNameLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string NormalizeMessageText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new HubException("Message text is required.");
        }

        var trimmed = text.Trim();
        if (trimmed.Length > MaxMessageLength)
        {
            throw new HubException($"Messages must be {MaxMessageLength} characters or fewer.");
        }

        return trimmed;
    }
}
