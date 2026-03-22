namespace Refraction.Api.Models;

public sealed record ChatMessage(
    string Id,
    string RoomSlug,
    ChatMessageKind Kind,
    string Text,
    string? DisplayName,
    DateTimeOffset SentAtUtc);
