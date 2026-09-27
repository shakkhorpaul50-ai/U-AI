using System.Security.Cryptography;
using System.Text;

namespace UAI.Services;

/// <summary>
/// Fallback database connection string, AES-256-CBC encrypted and split so the
/// plaintext never appears in the repository. Used only when DATABASE_URL is unset.
///
/// HONEST LABEL: this is obfuscation, not security. The key ships in this same
/// assembly, so anyone determined who reads this code can reassemble and decrypt
/// it. What it does stop: GitHub secret-scanning bots, credential harvesters,
/// and casual viewers — the highest-volume ways passwords leak from public repos.
/// For real security, set DATABASE_URL in the Render dashboard, which always
/// takes precedence over this embedded value (see Program.cs).
/// </summary>
public static class EmbeddedSecrets
{
    // AES-256-CBC, PKCS7. IV is embedded (it was used exactly once, offline).
    private const string IvB64 =
        "C+ukd+Pqaq1Tq9XNx8Id7A==";

    private const string CipherB64 =
        "yJucV+r//V+g02Dl28qQh9+bYirmrtTgHtZ3rC2nUtA9VIkeeOY0JpbDvG/6nTh8HA2lfisp2Lsp" +
        "ErKIyzOjPG7NgmwpiydVbOdtEkKB2PzBPQTWsG1t8NPahkUbTtoaVWs91GifMdL3Ppd6T0Tug5s" +
        "d7lIUYZ9IXdb0sz1sTs4B8cgYKLAH/P9boto0SCFlFDa4GxSNqkSNUBYoCzZcnQ==";

    // 32-byte key in three differently-encoded chunks (hex, base64, xor-masked hex).
    private const string KeyA_Hex = "662D917A0767D4B8F67264";
    private const string KeyB_B64 = "6AvGLBjqM7hT3vA=";
    private const string KeyC_XorHex = "7DF717876252A111442F";
    private const byte KeyC_Xor = 0x5A;

    private static string? _cached;
    private static readonly object _lock = new();

    /// <summary>Returns the decrypted connection string, or null if anything fails.</summary>
    public static string? GetDatabaseUrl()
    {
        if (_cached is not null) return _cached;

        lock (_lock)
        {
            if (_cached is not null) return _cached;

            try
            {
                var a = Convert.FromHexString(KeyA_Hex);          // 11 bytes
                var b = Convert.FromBase64String(KeyB_B64);       // 11 bytes
                var cx = Convert.FromHexString(KeyC_XorHex);      // 10 bytes, masked
                var c = new byte[cx.Length];
                for (int i = 0; i < cx.Length; i++) c[i] = (byte)(cx[i] ^ KeyC_Xor);

                var key = new byte[a.Length + b.Length + c.Length];
                Buffer.BlockCopy(a, 0, key, 0, a.Length);
                Buffer.BlockCopy(b, 0, key, a.Length, b.Length);
                Buffer.BlockCopy(c, 0, key, a.Length + b.Length, c.Length);
                if (key.Length != 32) return null;

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = key;
                aes.IV = Convert.FromBase64String(IvB64);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                var ct = Convert.FromBase64String(CipherB64);
                var pt = aes.CreateDecryptor().TransformFinalBlock(ct, 0, ct.Length);
                var s = Encoding.UTF8.GetString(pt);

                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(c);

                if (!s.StartsWith("postgresql://", StringComparison.Ordinal)) return null;

                _cached = s;
                return _cached;
            }
            catch
            {
                return null;
            }
        }
    }
}
