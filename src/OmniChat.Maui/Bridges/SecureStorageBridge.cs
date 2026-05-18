using System.Text.Json;

namespace OmniChat.Maui.Bridges;

public sealed class SecureStorageBridge
{
    public async Task<object?> InvokeAsync(string method, JsonElement? args)
    {
        var key = args?.GetProperty("key").GetString();
        if (string.IsNullOrEmpty(key)) return new { error = "key is required" };

        switch (method)
        {
            case "get":
            {
                var value = await SecureStorage.Default.GetAsync(key);
                return new { value };
            }
            case "set":
            {
                var value = args?.GetProperty("value").GetString() ?? string.Empty;
                await SecureStorage.Default.SetAsync(key, value);
                return new { ok = true };
            }
            case "delete":
            {
                SecureStorage.Default.Remove(key);
                return new { ok = true };
            }
            default:
                return new { error = $"Unknown method '{method}'." };
        }
    }
}
