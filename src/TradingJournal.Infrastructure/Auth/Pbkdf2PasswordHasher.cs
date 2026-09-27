using System.Globalization;
using System.Security.Cryptography;
using TradingJournal.Application.Auth;

namespace TradingJournal.Infrastructure.Auth;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Format = "pbkdf2-sha256-v1";
    private readonly byte[] dummySalt = RandomNumberGenerator.GetBytes(SaltSize);

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Format}${Iterations.ToString(CultureInfo.InvariantCulture)}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string? passwordHash)
    {
        var salt = dummySalt;
        var expected = new byte[HashSize];
        var wellFormed = false;
        if (passwordHash is { Length: <= 500 })
        {
            var parts = passwordHash.Split('$');
            if (parts.Length == 4 && parts[0] == Format && parts[1] == "600000")
            {
                try
                {
                    var parsedSalt = Convert.FromBase64String(parts[2]);
                    var parsedHash = Convert.FromBase64String(parts[3]);
                    if (parsedSalt.Length == SaltSize && parsedHash.Length == HashSize)
                    {
                        salt = parsedSalt;
                        expected = parsedHash;
                        wellFormed = true;
                    }
                }
                catch (FormatException) { /* Fail closed, while retaining password hashing work. */ }
            }
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(actual, expected) & wellFormed;
    }
}
