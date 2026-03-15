using System.Security.Cryptography;
using System.Text;

namespace SeaWars.Backend.Common.Crypto;

public static class PasswordHasher
{
    // PBKDF2-HMAC-SHA256 parameters.
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;

    public static string Hash(string password)
    {
        if (password is null)
            throw new ArgumentNullException(nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySize);

        return $"v1${Iterations}${Base64Url.Encode(salt)}${Base64Url.Encode(key)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        if (password is null)
            throw new ArgumentNullException(nameof(password));
        if (string.IsNullOrWhiteSpace(storedHash))
            return false;

        // Format: v1$<iterations>$<salt_b64url>$<key_b64url>
        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || parts[0] != "v1")
            return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expectedKey;
        try
        {
            salt = Base64Url.Decode(parts[2]);
            expectedKey = Base64Url.Decode(parts[3]);
        }
        catch
        {
            return false;
        }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: expectedKey.Length);

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}

