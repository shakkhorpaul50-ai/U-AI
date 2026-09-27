using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace UAI.Services;

/// <summary>
/// Single-round SHA-256 password hashing, chosen because this app runs on 0.1 CPU
/// where ASP.NET Identity's default PBKDF2 (100k iterations) costs about a second
/// per login.
///
/// This is deliberately weak: it is unsalted, so a leaked table is trivially
/// rainbow-tabled. Swap the body for PasswordHasher with
/// PasswordHasherOptions.IterationCount = 10_000 (about 60-100 ms on 0.1 CPU,
/// 10,000x more resistant to offline cracking) if this ever holds anything real.
/// The stored format is versioned so old hashes stay verifiable.
/// </summary>
public sealed class FastPasswordHasher<TUser> : IPasswordHasher<TUser> where TUser : class
{
    private const string Marker = "$sha256-v1$";

    public string HashPassword(TUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Marker + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword) || providedPassword is null)
            return PasswordVerificationResult.Failed;

        if (!hashedPassword.StartsWith(Marker, StringComparison.Ordinal))
        {
            // Not hashed by us. Refuse rather than silently accepting a plaintext row.
            return PasswordVerificationResult.Failed;
        }

        var expected = hashedPassword.AsSpan(Marker.Length);
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(providedPassword));

        if (CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected.ToString()), actual))
            return PasswordVerificationResult.Success;

        return PasswordVerificationResult.Failed;
    }
}
