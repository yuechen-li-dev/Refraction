using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Refraction.Api.Configuration;
using Refraction.Api.Models;
using Refraction.Api.Services;

namespace Refraction.Api.Tests;

public sealed class LiveKitTokenServiceTests
{
    private static readonly LiveKitOptions TestOptions = new()
    {
        Url = "wss://livekit.example.test",
        ApiKey = "test-api-key",
        ApiSecret = "0123456789abcdef0123456789abcdef"
    };

    [Fact]
    public void CreateHostToken_MintsPublishOnlyToken()
    {
        var service = CreateService();
        var session = CreateSession();

        var payload = ReadPayload(service.CreateHostToken(session));
        var videoGrants = payload.GetProperty("video");

        Assert.Equal($"host-{session.RoomSlug}", payload.GetProperty("sub").GetString());
        Assert.Equal(session.RoomId, videoGrants.GetProperty("room").GetString());
        Assert.True(videoGrants.GetProperty("roomJoin").GetBoolean());
        Assert.True(videoGrants.GetProperty("canPublish").GetBoolean());
        Assert.False(videoGrants.GetProperty("canSubscribe").GetBoolean());
        Assert.False(videoGrants.GetProperty("canPublishData").GetBoolean());
    }

    [Fact]
    public void CreateViewerToken_MintsUniqueSubscribeOnlyTokensForSameSession()
    {
        var service = CreateService();
        var session = CreateSession();

        var firstToken = service.CreateViewerToken(session);
        var secondToken = service.CreateViewerToken(session);

        Assert.NotEqual(firstToken, secondToken);

        var firstPayload = ReadPayload(firstToken);
        var secondPayload = ReadPayload(secondToken);
        var firstVideoGrants = firstPayload.GetProperty("video");
        var secondVideoGrants = secondPayload.GetProperty("video");

        Assert.StartsWith("viewer-", firstPayload.GetProperty("sub").GetString());
        Assert.StartsWith("viewer-", secondPayload.GetProperty("sub").GetString());
        Assert.NotEqual(firstPayload.GetProperty("sub").GetString(), secondPayload.GetProperty("sub").GetString());

        Assert.Equal(session.RoomId, firstVideoGrants.GetProperty("room").GetString());
        Assert.Equal(session.RoomId, secondVideoGrants.GetProperty("room").GetString());
        Assert.True(firstVideoGrants.GetProperty("roomJoin").GetBoolean());
        Assert.True(secondVideoGrants.GetProperty("roomJoin").GetBoolean());
        Assert.False(firstVideoGrants.GetProperty("canPublish").GetBoolean());
        Assert.False(secondVideoGrants.GetProperty("canPublish").GetBoolean());
        Assert.True(firstVideoGrants.GetProperty("canSubscribe").GetBoolean());
        Assert.True(secondVideoGrants.GetProperty("canSubscribe").GetBoolean());
        Assert.False(firstVideoGrants.GetProperty("canPublishData").GetBoolean());
        Assert.False(secondVideoGrants.GetProperty("canPublishData").GetBoolean());
    }

    private static LiveKitTokenService CreateService() => new(TestOptions, NullLogger<LiveKitTokenService>.Instance);

    private static RoomSession CreateSession() => new(
        "room-test",
        "slugtest",
        DateTimeOffset.Parse("2026-03-22T00:00:00Z"),
        null,
        null);

    private static JsonElement ReadPayload(string jwt)
    {
        var segments = jwt.Split('.');
        Assert.True(segments.Length >= 2, "JWT payload segment was missing.");

        var payload = segments[1]
            .Replace('-', '+')
            .Replace('_', '/');

        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
        return document.RootElement.Clone();
    }
}
