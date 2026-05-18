using System.Text.Json;

namespace OmniChat.Maui.Bridges;

/// <summary>
/// Single dispatch point for `window.omnichat.*` calls coming from the WebView.
/// Each bridge owns one of: secure-storage, share, file-picker, haptic, biometric.
/// </summary>
public sealed class BridgeRouter
{
    private readonly SecureStorageBridge _secureStorage;
    private readonly ShareBridge _share;
    private readonly FilePickerBridge _filePicker;
    private readonly HapticBridge _haptic;
    private readonly BiometricBridge _biometric;

    public BridgeRouter(
        SecureStorageBridge secureStorage,
        ShareBridge share,
        FilePickerBridge filePicker,
        HapticBridge haptic,
        BiometricBridge biometric)
    {
        _secureStorage = secureStorage;
        _share = share;
        _filePicker = filePicker;
        _haptic = haptic;
        _biometric = biometric;
    }

    public Task<object?> InvokeAsync(string bridge, string method, JsonElement? args)
    {
        return bridge switch
        {
            "secure-storage" => _secureStorage.InvokeAsync(method, args),
            "share"          => _share.InvokeAsync(method, args),
            "file-picker"    => _filePicker.InvokeAsync(method, args),
            "haptic"         => _haptic.InvokeAsync(method, args),
            "biometric"      => _biometric.InvokeAsync(method, args),
            _ => Task.FromResult<object?>(new { error = $"Unknown bridge '{bridge}'." })
        };
    }
}
