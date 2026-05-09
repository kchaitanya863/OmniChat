namespace OmniChat.Core.Models;

public sealed class ChatSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "New Chat";

    public List<ChatMessage> Messages { get; } = [];
}
