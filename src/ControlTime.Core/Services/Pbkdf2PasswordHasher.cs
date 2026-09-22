using System.Security.Cryptography;
using System.Text;
using ControlTime.Core.Abstractions;

namespace ControlTime.Core.Services;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int Iterations = 100_000;

    private const int SaltSize = 16;
    private const int KeySize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = DeriveKey(password, salt, Iterations);

        return string.Join('.',
            "PBKDF2",
            Algorithm.ToString(),
            Iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(key));
    }

    public bool Verify(string password, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (!TryParse(passwordHash, out int iterations, out byte[]? salt, out byte[]? expectedKey))
        {
            return false;
        }

        byte[] actualKey = DeriveKey(password, salt!, iterations);
        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }

    public bool IsValidFormat(string? passwordHash)
        => TryParse(passwordHash, out _, out _, out _);

    private static bool TryParse(
        string? passwordHash,
        out int iterations,
        out byte[]? salt,
        out byte[]? key)
    {
        iterations = 0;
        salt = null;
        key = null;

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        string[] parts = passwordHash.Split('.');
        if (parts.Length != 5 || parts[0] != "PBKDF2" || parts[1] != Algorithm.ToString())
        {
            return false;
        }

        if (!int.TryParse(parts[2], out iterations) || iterations <= 0)
        {
            iterations = 0;
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[3]);
            key = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            salt = null;
            key = null;
            return false;
        }

        if (salt.Length == 0 || key.Length == 0)
        {
            salt = null;
            key = null;
            return false;
        }

        return true;
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            Algorithm,
            KeySize);
    }
}
