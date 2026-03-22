using System.Collections.Concurrent;

namespace Refraction.Api.Services.Chat;

public sealed class InMemoryChatConnectionStore : IChatConnectionStore
{
    private readonly ConcurrentDictionary<string, ChatConnectionInfo> connections = new(StringComparer.Ordinal);

    public void Set(string connectionId, ChatConnectionInfo connectionInfo)
    {
        connections[connectionId] = connectionInfo;
    }

    public ChatConnectionInfo? Get(string connectionId)
    {
        return connections.TryGetValue(connectionId, out var connectionInfo) ? connectionInfo : null;
    }

    public ChatConnectionInfo? UpdateDisplayName(string connectionId, string displayName)
    {
        while (connections.TryGetValue(connectionId, out var current))
        {
            var next = current with { DisplayName = displayName };
            if (connections.TryUpdate(connectionId, next, current))
            {
                return next;
            }
        }

        return null;
    }

    public void Remove(string connectionId)
    {
        connections.TryRemove(connectionId, out _);
    }
}
