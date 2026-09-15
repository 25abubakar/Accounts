using System.Security.Cryptography;

namespace Accounts.Services.Services;

public static class ProcessAuthorityPinHasher
{
    private const int Iterations = 120_000;

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "v1" ||
            !int.TryParse(parts[1], out var iterations) || iterations != Iterations) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }
}
