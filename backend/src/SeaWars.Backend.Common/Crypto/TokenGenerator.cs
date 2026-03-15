using System.Security.Cryptography;
using System.Text;

namespace SeaWars.Backend.Common.Crypto;

public static class TokenGenerator
{
    public static string CreateOpaqueToken(int numBytes = 32)
    {
        if (numBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(numBytes), "Must be greater than zero.");

        var bytes = RandomNumberGenerator.GetBytes(numBytes);
        return Base64Url.Encode(bytes);
    }

    public static string Sha256Base64Url(string input)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Base64Url.Encode(hash);
    }
}

