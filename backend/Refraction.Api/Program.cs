using Refraction.Api.Configuration;
using Refraction.Api.Contracts;
using Refraction.Api.Hubs;
using Refraction.Api.Models;
using Refraction.Api.Services;
using Refraction.Api.Services.Chat;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.SingleLine = true;
    options.IncludeScopes = false;
});

var liveKitOptions = BuildLiveKitOptions(builder.Configuration);
var appOptions = BuildAppOptions(builder.Configuration);

builder.Services.AddSingleton(liveKitOptions);
builder.Services.AddSingleton(appOptions);
var roomSessionOptions = BuildRoomSessionOptions(builder.Configuration);
builder.Services.AddSingleton(roomSessionOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<InMemoryRoomSessionStore>();
builder.Services.AddSingleton<IRoomSessionStore>(static serviceProvider => serviceProvider.GetRequiredService<InMemoryRoomSessionStore>());
builder.Services.AddSingleton<IRoomChatStore, InMemoryRoomChatStore>();
builder.Services.AddSingleton<IChatConnectionStore, InMemoryChatConnectionStore>();
builder.Services.AddHostedService<RoomSessionCleanupService>();
builder.Services.AddSingleton<ILiveKitTokenService, LiveKitTokenService>();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(appOptions.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/rooms", async (
    CreateRoomRequest _,
    IRoomSessionStore store,
    ILiveKitTokenService tokenService,
    AppOptions options,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("Rooms");
    var session = store.Create();

    await tokenService.EnsureRoomExistsAsync(session, cancellationToken);

    logger.LogInformation("Created room {RoomId} with slug {RoomSlug}.", session.RoomId, session.RoomSlug);

    return Results.Ok(new CreateRoomResponse(
        session.RoomId,
        session.RoomSlug,
        tokenService.CreateHostToken(session),
        $"{options.PublicAppBaseUrl.TrimEnd('/')}/r/{session.RoomSlug}",
        options.LiveKitUrl,
        session.State));
});

app.MapGet("/api/rooms/{slug}", (
    string slug,
    IRoomSessionStore store,
    ILiveKitTokenService tokenService,
    AppOptions options) =>
{
    var session = store.GetBySlug(slug);
    if (session is null)
    {
        return Results.NotFound(new ErrorResponse("room_not_found", "Room link is invalid or has expired.", RoomState.Error));
    }

    var viewerToken = session.State == RoomState.Ended ? null : tokenService.CreateViewerToken(session);
    var message = session.State switch
    {
        RoomState.Waiting => "Waiting for the host to start streaming.",
        RoomState.Live => "Host is live.",
        RoomState.Ended => "This stream has ended.",
        _ => "Unable to resolve room."
    };

    return Results.Ok(new ResolveRoomResponse(
        session.RoomId,
        session.RoomSlug,
        viewerToken,
        options.LiveKitUrl,
        session.State,
        message));
});

app.MapPost("/api/rooms/{slug}/state", async (
    string slug,
    UpdateRoomStateRequest request,
    IRoomSessionStore store,
    IRoomChatStore chatStore,
    IHubContext<ChatHub, IChatClient> chatHub) =>
{
    var result = store.TryUpdateState(slug, request.State);

    if (result.Status == RoomStateUpdateStatus.Updated && result.Session is not null)
    {
        if (result.Session.State == RoomState.Live)
        {
            var systemMessage = chatStore.AddSystemMessage(result.Session.RoomSlug, "Stream started.");
            await chatHub.Clients.Group(result.Session.RoomSlug).ReceiveMessage(ChatContracts.Map(systemMessage));
        }
        else if (result.Session.State == RoomState.Ended)
        {
            var systemMessage = chatStore.AddSystemMessage(result.Session.RoomSlug, "Stream ended. Chat is closed.");
            await chatHub.Clients.Group(result.Session.RoomSlug).ReceiveMessage(ChatContracts.Map(systemMessage));
            await chatHub.Clients.Group(result.Session.RoomSlug)
                .ChatClosed(new ChatClosedEvent(result.Session.RoomSlug, RoomState.Ended, "Stream ended. Chat is closed."));
            chatStore.ClearRoom(result.Session.RoomSlug);
        }
    }

    return result.Status switch
    {
        RoomStateUpdateStatus.Updated => Results.Ok(new ResolveRoomResponse(
            result.Session!.RoomId,
            result.Session.RoomSlug,
            null,
            string.Empty,
            result.Session.State,
            result.Message)),
        RoomStateUpdateStatus.NotFound => Results.NotFound(new ErrorResponse(result.Code, result.Message, RoomState.Error)),
        RoomStateUpdateStatus.InvalidTransition => Results.Conflict(new ErrorResponse(result.Code, result.Message, result.Session!.State)),
        RoomStateUpdateStatus.UnsupportedState => Results.BadRequest(new ErrorResponse(result.Code, result.Message, RoomState.Error)),
        _ => Results.BadRequest(new ErrorResponse("unsupported_room_state", "Only 'live' and 'ended' state updates are supported.", RoomState.Error))
    };
});

app.MapPost("/api/rooms/{slug}/end", async (
    string slug,
    IRoomSessionStore store,
    IRoomChatStore chatStore,
    IHubContext<ChatHub, IChatClient> chatHub) =>
{
    var session = store.MarkEnded(slug);
    if (session is null)
    {
        return Results.NotFound(new ErrorResponse("room_not_found", "Room link is invalid or has expired.", RoomState.Error));
    }

    var systemMessage = chatStore.AddSystemMessage(session.RoomSlug, "Stream ended. Chat is closed.");
    await chatHub.Clients.Group(session.RoomSlug).ReceiveMessage(ChatContracts.Map(systemMessage));
    await chatHub.Clients.Group(session.RoomSlug)
        .ChatClosed(new ChatClosedEvent(session.RoomSlug, session.State, "Stream ended. Chat is closed."));
    chatStore.ClearRoom(session.RoomSlug);

    return Results.Ok(new RoomEndedResponse(session.RoomSlug, session.State));
});

app.MapHub<ChatHub>("/hubs/chat");

app.Run();

static LiveKitOptions BuildLiveKitOptions(IConfiguration configuration)
{
    var url = GetRequired(configuration, "LIVEKIT_URL");
    var apiKey = GetRequired(configuration, "LIVEKIT_API_KEY");
    var apiSecret = GetRequired(configuration, "LIVEKIT_API_SECRET");

    return new LiveKitOptions
    {
        Url = url,
        ApiKey = apiKey,
        ApiSecret = apiSecret
    };
}

static RoomSessionOptions BuildRoomSessionOptions(IConfiguration configuration)
{
    return new RoomSessionOptions
    {
        WaitingRoomTtl = GetOptionalTimeSpan(configuration, "ROOM_SESSION_WAITING_TTL_MINUTES", TimeSpan.FromMinutes(15), 1),
        LiveRoomTtl = GetOptionalTimeSpan(configuration, "ROOM_SESSION_LIVE_TTL_MINUTES", TimeSpan.FromHours(4), 1),
        EndedRoomRetention = GetOptionalTimeSpan(configuration, "ROOM_SESSION_ENDED_RETENTION_MINUTES", TimeSpan.FromMinutes(10), 1),
        CleanupInterval = GetOptionalTimeSpan(configuration, "ROOM_SESSION_CLEANUP_INTERVAL_SECONDS", TimeSpan.FromSeconds(30), 1)
    };
}

static AppOptions BuildAppOptions(IConfiguration configuration)
{
    var publicAppBaseUrl = GetRequired(configuration, "PUBLIC_APP_BASE_URL");
    var liveKitUrl = GetRequired(configuration, "LIVEKIT_URL");
    var configuredOrigins = configuration["CORS_ALLOWED_ORIGINS"];

    var origins = string.IsNullOrWhiteSpace(configuredOrigins)
        ? [publicAppBaseUrl]
        : configuredOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    return new AppOptions
    {
        PublicAppBaseUrl = publicAppBaseUrl,
        LiveKitUrl = liveKitUrl,
        AllowedOrigins = origins
    };
}

static TimeSpan GetOptionalTimeSpan(IConfiguration configuration, string key, TimeSpan fallback, double minimumValue)
{
    var configuredValue = configuration[key];
    if (string.IsNullOrWhiteSpace(configuredValue))
    {
        return fallback;
    }

    if (!double.TryParse(configuredValue, out var numericValue) || numericValue < minimumValue)
    {
        throw new InvalidOperationException($"Configuration value '{key}' must be a number greater than or equal to {minimumValue}.");
    }

    return key.EndsWith("_SECONDS", StringComparison.Ordinal)
        ? TimeSpan.FromSeconds(numericValue)
        : TimeSpan.FromMinutes(numericValue);
}

static string GetRequired(IConfiguration configuration, string key)
{
    var value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Configuration value '{key}' is required.");
    }

    return value;
}

public partial class Program;
