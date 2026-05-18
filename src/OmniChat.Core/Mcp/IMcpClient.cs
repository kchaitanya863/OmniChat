namespace OmniChat.Core.Mcp;

public interface IMcpClient : IAsyncDisposable
{
    Task ConnectAsync(McpServerConfig server, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<McpToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken = default);
}

public sealed record McpServerConfig(string Id, string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string>? Env = null);

public sealed record McpToolDescriptor(string Name, string? Description, IReadOnlyDictionary<string, object?>? InputSchema);

public sealed record McpToolResult(bool IsError, string Content);
