using System.Security.Cryptography;

namespace TallyAuditAssistant.Data.Security;

public static class PasswordHasher
{
    private const int SaltSize = 32; // 256-bit salt
    private const int HashSize = 32; // 256-bit subkey
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static (string Hash, string Salt) HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool VerifyPassword(string password, string storedHashBase64, string storedSaltBase64)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHashBase64) || string.IsNullOrEmpty(storedSaltBase64))
            return false;

        try
        {
            byte[] salt = Convert.FromBase64String(storedSaltBase64);
            byte[] expectedHash = Convert.FromBase64String(storedHashBase64);

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
