using System.Text.Json;

namespace OmniChat.Maui.Bridges;

public sealed class FilePickerBridge
{
    public async Task<object?> InvokeAsync(string method, JsonElement? args)
    {
        if (method != "pick") return new { error = $"Unknown method '{method}'." };

        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Attach a document"
        });
        if (result is null) return new { cancelled = true };

        await using var stream = await result.OpenReadAsync();
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var bytes = memory.ToArray();
        return new
        {
            name = result.FileName,
            mime = result.ContentType,
            base64 = Convert.ToBase64String(bytes)
        };
    }
}
