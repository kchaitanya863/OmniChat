using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OmniChat.Core.Mcp;

/// <summary>
/// Minimal MCP client over stdio JSON-RPC 2.0. Tracks request IDs, dispatches responses
/// back to pending TaskCompletionSources. Server lifecycle is owned by this client —
/// dispose to stop it cleanly.
/// </summary>
public sealed class StdioMcpClient : IMcpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private Process? _process;
    private CancellationTokenSource? _readerCts;
    private Task? _readerLoop;
    private long _nextId;
    private readonly object _writeGate = new();
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly object _pendingGate = new();

    public async Task ConnectAsync(McpServerConfig server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var psi = new ProcessStartInfo(server.Command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8
        };
        foreach (var arg in server.Args) psi.ArgumentList.Add(arg);
        if (server.Env is not null)
        {
            foreach (var (k, v) in server.Env) psi.Environment[k] = v;
        }

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch MCP server '{server.Command}'.");

        _readerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readerLoop = ReadLoopAsync(_process.StandardOutput, _readerCts.Token);

        // Send "initialize" per MCP spec.
        await CallAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "OmniChat", version = "0.1" }
        }, cancellationToken);

        // notifications/initialized
        Send(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized",
            @params = new { }
        });
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("tools/list", new { }, cancellationToken);
        if (!result.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<McpToolDescriptor>();
        }

        var list = new List<McpToolDescriptor>(tools.GetArrayLength());
        foreach (var t in tools.EnumerateArray())
        {
            var name = t.GetProperty("name").GetString() ?? string.Empty;
            var desc = t.TryGetProperty("description", out var d) ? d.GetString() : null;
            IReadOnlyDictionary<string, object?>? schema = null;
            if (t.TryGetProperty("inputSchema", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                schema = JsonSerializer.Deserialize<Dictionary<string, object?>>(s.GetRawText(), JsonOptions);
            }
            list.Add(new McpToolDescriptor(name, desc, schema));
        }
        return list;
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?>? arguments, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("tools/call", new
        {
            name = toolName,
            arguments = arguments ?? new Dictionary<string, object?>()
        }, cancellationToken);

        var isError = result.TryGetProperty("isError", out var err) && err.GetBoolean();
        var sb = new StringBuilder();
        if (result.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in contentArr.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    sb.AppendLine(text.GetString());
                }
            }
        }
        return new McpToolResult(isError, sb.ToString().TrimEnd());
    }

    private async Task<JsonElement> CallAsync(string method, object @params, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingGate) _pending[id] = tcs;

        Send(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        });

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        var response = await tcs.Task.ConfigureAwait(false);
        if (response.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(error.GetRawText());
        }
        return response.GetProperty("result");
    }

    private void Send(object payload)
    {
        if (_process?.StandardInput is null) return;
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        lock (_writeGate)
        {
            _process.StandardInput.WriteLine(json);
            _process.StandardInput.Flush();
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken token)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(token)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;

                var id = idEl.GetInt64();
                TaskCompletionSource<JsonElement>? tcs;
                lock (_pendingGate)
                {
                    _pending.Remove(id, out tcs);
                }
                tcs?.TrySetResult(root.Clone());
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            lock (_pendingGate)
            {
                foreach (var tcs in _pending.Values) tcs.TrySetException(ex);
                _pending.Clear();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _readerCts?.Cancel();
        if (_readerLoop is not null) await _readerLoop.ConfigureAwait(false);
        _readerCts?.Dispose();

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.Close();
                    if (!_process.WaitForExit(2000)) _process.Kill();
                }
            }
            catch { /* best-effort */ }
            _process.Dispose();
        }
    }
}
