namespace OmniChat.Core.Security;

/// <summary>
/// Cross-platform encrypted key store. Stores BYOK provider keys at rest, returns
/// the plaintext only when explicitly unsealed. Implementations must never persist
/// plaintext to disk.
/// </summary>
public interface IKeyVault
{
    /// <summary>Encrypts <paramref name="plaintext"/> using a platform-specific key.</summary>
    string Seal(string plaintext);

    /// <summary>Decrypts a previously sealed payload. Returns null on failure (corrupted, wrong machine, key rotated).</summary>
    string? Unseal(string cipherText);

    /// <summary>Stable fingerprint of the plaintext key, safe to surface in the UI.</summary>
    string Fingerprint(string plaintext);
}
