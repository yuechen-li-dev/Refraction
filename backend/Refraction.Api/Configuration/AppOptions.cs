namespace Refraction.Api.Configuration;

public sealed class AppOptions
{
    public required string PublicAppBaseUrl { get; init; }
    public required string LiveKitUrl { get; init; }
    public string[] AllowedOrigins { get; init; } = [];
}
