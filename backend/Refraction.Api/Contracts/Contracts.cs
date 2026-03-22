using Refraction.Api.Models;


namespace Refraction.Api.Contracts;

public sealed record CreateRoomRequest;

public sealed record CreateRoomResponse(
    string RoomId,
    string RoomSlug,
    string HostToken,
    string ViewerUrl,
    string LiveKitUrl,
    RoomState State);

public sealed record ResolveRoomResponse(
    string RoomId,
    string RoomSlug,
    string? ViewerToken,
    string LiveKitUrl,
    RoomState State,
    string? Message);

public sealed record UpdateRoomStateRequest(RoomState State);

public sealed record RoomEndedResponse(string RoomSlug, RoomState State);

public sealed record ErrorResponse(string Code, string Message, RoomState State);

public sealed record JoinChatRequest(string RoomSlug, string DisplayName);

public sealed record UpdateChatDisplayNameRequest(string RoomSlug, string DisplayName);

public sealed record SendChatMessageRequest(string Text);

public sealed record ChatMessageDto(
    string Id,
    string RoomSlug,
    string Kind,
    string Text,
    string? DisplayName,
    DateTimeOffset SentAtUtc);

public sealed record ChatClosedEvent(string RoomSlug, RoomState State, string Message);

public interface IChatClient
{
    Task ReceiveRecentMessages(IEnumerable<ChatMessageDto> messages);
    Task ReceiveMessage(ChatMessageDto message);
    Task DisplayNameUpdated(string displayName);
    Task ChatClosed(ChatClosedEvent closedEvent);
}

public static class ChatContracts
{
    public static ChatMessageDto Map(ChatMessage message) => new(
        message.Id,
        message.RoomSlug,
        message.Kind == ChatMessageKind.System ? "system" : "user",
        message.Text,
        message.DisplayName,
        message.SentAtUtc);
}
