namespace Refraction.Api.Services.Chat;

public interface IChatConnectionStore
{
    void Set(string connectionId, ChatConnectionInfo connectionInfo);
    ChatConnectionInfo? Get(string connectionId);
    ChatConnectionInfo? UpdateDisplayName(string connectionId, string displayName);
    void Remove(string connectionId);
}
