using System.Security.Cryptography;
using System.Text;

namespace OmniChat.Core.Security;

/// <summary>
/// Computes a short, stable, non-reversible identifier for a secret value.
/// Used as a UI-safe surface ("sha256:a1b2c3…") so users can see *which* key is configured
/// without ever rendering the key itself.
/// </summary>
public static class KeyFingerprint
{
    public static string Compute(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var hash = SHA256.HashData(bytes);

        var sb = new StringBuilder("sha256:", capacity: 19);
        for (var i = 0; i < 6; i++)
        {
            sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
