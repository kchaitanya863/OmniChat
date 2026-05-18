using System.Text.Json;

namespace OmniChat.Maui.Bridges;

/// <summary>
/// Biometric unlock bridge. Real impl requires Plugin.Fingerprint (or platform LocalAuthentication APIs).
/// For now this returns a graceful "unsupported" so the web shell can fall back to a passphrase prompt.
/// </summary>
public sealed class BiometricBridge
{
    public Task<object?> InvokeAsync(string method, JsonElement? args)
    {
        if (method != "unlock") return Task.FromResult<object?>(new { error = $"Unknown method '{method}'." });
        return Task.FromResult<object?>(new { unsupported = true, reason = "Biometric plugin not bundled in this build." });
    }
}
