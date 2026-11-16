using System.Security.Cryptography;

namespace Pingo.Identity.Passwords;

internal record PasswordHash(byte[] Hash, byte[] Salt, int Iterations);

// Hashes and checks passwords. An interface so the login logic can be unit-tested without the
// deliberately slow real hashing.
internal interface IPasswordHasher
{
    PasswordHash Hash(string password);

    bool Verify(string password, PasswordHash stored);
}

// PBKDF2-HMAC-SHA256 with a random salt per password, built into .NET.
// - The salt makes equal passwords hash differently, so precomputed tables are useless.
// - The iteration count makes every guess slow. 600 000 is OWASP's recommendation for this algorithm.
// - The count is stored with each hash, so it can be raised later without breaking existing passwords.
internal sealed class PasswordHasher : IPasswordHasher
{
    public const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public PasswordHash Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return new PasswordHash(hash, salt, Iterations);
    }

    public bool Verify(string password, PasswordHash stored)
    {
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, stored.Salt, stored.Iterations, Algorithm, stored.Hash.Length);
        // Constant-time comparison, so response timing doesn't reveal how much of the hash matched.
        return CryptographicOperations.FixedTimeEquals(hash, stored.Hash);
    }
}
