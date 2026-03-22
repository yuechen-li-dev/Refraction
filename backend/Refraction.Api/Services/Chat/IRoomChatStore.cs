using Refraction.Api.Models;

namespace Refraction.Api.Services.Chat;

public interface IRoomChatStore
{
    IReadOnlyList<ChatMessage> GetRecentMessages(string roomSlug);
    ChatMessage AddUserMessage(string roomSlug, string displayName, string text);
    ChatMessage AddSystemMessage(string roomSlug, string text);
    void ClearRoom(string roomSlug);
    int CleanupInactiveRooms(Func<string, bool> isActiveRoom);
}
