using System.Security.Cryptography;
using System.Text;

namespace ArqamBase.Data;

/// <summary>Salted PBKDF2-SHA256 password hashes. Passwords cannot be read back, only checked.</summary>
public static class PasswordHasher
{
    public const int Iterations = 210_000;
    public const int MinLength = 8;

    public static (byte[] Hash, byte[] Salt, int Iterations) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return (Derive(password, salt, Iterations), salt, Iterations);
    }

    public static bool Verify(string password, byte[] hash, byte[] salt, int iterations) =>
        CryptographicOperations.FixedTimeEquals(Derive(password, salt, iterations), hash);

    /// <summary>Returns a reason the password is too weak, or null when it is acceptable.</summary>
    public static string? Weakness(string password) =>
        password.Length < MinLength ? $"Use at least {MinLength} characters."
        : !password.Any(char.IsLetter) || !password.Any(char.IsDigit) ? "Use both letters and numbers."
        : null;

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
}
