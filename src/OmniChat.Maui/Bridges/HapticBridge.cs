using System.Text.Json;

namespace OmniChat.Maui.Bridges;

public sealed class HapticBridge
{
    public Task<object?> InvokeAsync(string method, JsonElement? args)
    {
        if (method != "play") return Task.FromResult<object?>(new { error = $"Unknown method '{method}'." });

        var kind = args?.GetProperty("kind").GetString() ?? "light";
        try
        {
            switch (kind)
            {
                case "click":
                case "light":
                    HapticFeedback.Default.Perform(HapticFeedbackType.Click);
                    break;
                case "long":
                case "success":
                    HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
                    break;
            }
        }
        catch
        {
            // Some platforms / contexts don't support haptics. Silent best-effort.
        }
        return Task.FromResult<object?>(new { ok = true });
    }
}
