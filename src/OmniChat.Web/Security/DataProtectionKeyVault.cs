using Microsoft.AspNetCore.DataProtection;
using OmniChat.Core.Security;

namespace OmniChat.Web.Security;

public sealed class DataProtectionKeyVault : IKeyVault
{
    private const string Purpose = "OmniChat.ProviderKeys.v1";

    private readonly IDataProtector _protector;

    public DataProtectionKeyVault(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public string Seal(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return _protector.Protect(plaintext);
    }

    public string? Unseal(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(cipherText);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public string Fingerprint(string plaintext) => KeyFingerprint.Compute(plaintext);
}
