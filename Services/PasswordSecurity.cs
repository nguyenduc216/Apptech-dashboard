using System.Security.Cryptography;
using System.Text;

namespace ApptechDashboard.Services;

public enum PasswordVerificationStatus
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2
}

public static class PasswordSecurity
{
    private const string AlgorithmMarker = "pbkdf2-sha256";
    private const int DefaultIterations = 120_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return string.Join(
            '$',
            AlgorithmMarker,
            DefaultIterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static PasswordVerificationStatus VerifyPassword(string password, string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedValue))
        {
            return PasswordVerificationStatus.Failed;
        }

        var normalizedStoredValue = storedValue.Trim();

        if (normalizedStoredValue.StartsWith($"{AlgorithmMarker}$", StringComparison.OrdinalIgnoreCase))
        {
            return VerifyPbkdf2(password, normalizedStoredValue);
        }

        return VerifyLegacyPassword(password, normalizedStoredValue)
            ? PasswordVerificationStatus.SuccessRehashNeeded
            : PasswordVerificationStatus.Failed;
    }

    private static PasswordVerificationStatus VerifyPbkdf2(string password, string storedValue)
    {
        var parts = storedValue.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 4 ||
            !parts[0].Equals(AlgorithmMarker, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations < 10_000)
        {
            return PasswordVerificationStatus.Failed;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var storedHash = Convert.FromBase64String(parts[3]);
            var computedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                storedHash.Length);

            if (!CryptographicOperations.FixedTimeEquals(computedHash, storedHash))
            {
                return PasswordVerificationStatus.Failed;
            }

            return iterations < DefaultIterations
                ? PasswordVerificationStatus.SuccessRehashNeeded
                : PasswordVerificationStatus.Success;
        }
        catch (FormatException)
        {
            return PasswordVerificationStatus.Failed;
        }
    }

    private static bool VerifyLegacyPassword(string password, string storedValue)
    {
        if (FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(storedValue)))
        {
            return true;
        }

        return MatchesBase64Hash(password, storedValue, MD5.HashData) ||
               MatchesBase64Hash(password, storedValue, SHA1.HashData) ||
               MatchesBase64Hash(password, storedValue, SHA256.HashData) ||
               MatchesHexHash(password, storedValue, MD5.HashData) ||
               MatchesHexHash(password, storedValue, SHA1.HashData) ||
               MatchesHexHash(password, storedValue, SHA256.HashData);
    }

    private static bool MatchesBase64Hash(
        string password,
        string storedValue,
        Func<byte[], byte[]> hashFactory)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var encoded = Convert.ToBase64String(hashFactory(passwordBytes));
        return FixedTimeEquals(Encoding.UTF8.GetBytes(encoded), Encoding.UTF8.GetBytes(storedValue));
    }

    private static bool MatchesHexHash(
        string password,
        string storedValue,
        Func<byte[], byte[]> hashFactory)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var encoded = Convert.ToHexString(hashFactory(passwordBytes));
        return FixedTimeEquals(
            Encoding.UTF8.GetBytes(encoded),
            Encoding.UTF8.GetBytes(storedValue.ToUpperInvariant()));
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
