using System.Security.Cryptography;
using System.Text;

namespace SeaWars.Backend.Common.Crypto;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var s = Convert.ToBase64String(data);
        return s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static byte[] Decode(string base64Url)
    {
        if (string.IsNullOrWhiteSpace(base64Url))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(base64Url));

        var s = base64Url.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (s.Length % 4);
        if (padding is > 0 and < 4)
            s = s.PadRight(s.Length + padding, '=');

        return Convert.FromBase64String(s);
    }
}

