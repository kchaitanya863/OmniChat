using System.Text.Json;

namespace OmniChat.Maui.Bridges;

public sealed class ShareBridge
{
    public async Task<object?> InvokeAsync(string method, JsonElement? args)
    {
        if (method != "share") return new { error = $"Unknown method '{method}'." };
        var text = args?.GetProperty("text").GetString() ?? string.Empty;
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = text,
            Title = "OmniChat"
        });
        return new { ok = true };
    }
}
