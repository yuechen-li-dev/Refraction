using System.Security.Cryptography;
using System.Text;

namespace Refraction.Api.Services;

public static class RoomSlugGenerator
{
    private const string AllowedCharacters = "abcdefghjkmnpqrstuvwxyz23456789";

    public static string Create(int length = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 4);

        Span<byte> buffer = stackalloc byte[length];
        RandomNumberGenerator.Fill(buffer);

        var builder = new StringBuilder(length);
        foreach (var value in buffer)
        {
            builder.Append(AllowedCharacters[value % AllowedCharacters.Length]);
        }

        return builder.ToString();
    }
}
