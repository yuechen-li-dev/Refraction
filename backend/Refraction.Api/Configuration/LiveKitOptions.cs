namespace Refraction.Api.Configuration;

public sealed class LiveKitOptions
{
    public required string Url { get; init; }
    public required string ApiKey { get; init; }
    public required string ApiSecret { get; init; }
}
