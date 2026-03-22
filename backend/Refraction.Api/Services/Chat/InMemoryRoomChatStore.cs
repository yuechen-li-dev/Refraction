using System.Collections.Concurrent;
using Refraction.Api.Models;

namespace Refraction.Api.Services.Chat;

public sealed class InMemoryRoomChatStore : IRoomChatStore
{
    private const int MaxMessagesPerRoom = 50;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ChatMessage>> roomBuffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider timeProvider;

    public InMemoryRoomChatStore(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public IReadOnlyList<ChatMessage> GetRecentMessages(string roomSlug)
    {
        if (!roomBuffers.TryGetValue(roomSlug, out var buffer))
        {
            return [];
        }

        return [.. buffer.OrderBy(message => message.SentAtUtc)];
    }

    public ChatMessage AddUserMessage(string roomSlug, string displayName, string text)
    {
        return AddMessage(roomSlug, ChatMessageKind.User, text, displayName);
    }

    public ChatMessage AddSystemMessage(string roomSlug, string text)
    {
        return AddMessage(roomSlug, ChatMessageKind.System, text, null);
    }

    public void ClearRoom(string roomSlug)
    {
        roomBuffers.TryRemove(roomSlug, out _);
    }

    public int CleanupInactiveRooms(Func<string, bool> isActiveRoom)
    {
        var removed = 0;

        foreach (var roomSlug in roomBuffers.Keys)
        {
            if (isActiveRoom(roomSlug))
            {
                continue;
            }

            if (roomBuffers.TryRemove(roomSlug, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private ChatMessage AddMessage(string roomSlug, ChatMessageKind kind, string text, string? displayName)
    {
        var message = new ChatMessage(
            $"msg-{Guid.NewGuid():N}",
            roomSlug,
            kind,
            text,
            displayName,
            timeProvider.GetUtcNow());

        var buffer = roomBuffers.GetOrAdd(roomSlug, static _ => new ConcurrentQueue<ChatMessage>());
        buffer.Enqueue(message);

        while (buffer.Count > MaxMessagesPerRoom && buffer.TryDequeue(out _))
        {
        }

        return message;
    }
}
