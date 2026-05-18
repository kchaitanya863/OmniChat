using System.Text.Json;
using OmniChat.Maui.Bridges;

namespace OmniChat.Maui;

public partial class MainPage : ContentPage
{
    private readonly BridgeRouter _router;

    public MainPage(BridgeRouter router)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        InitializeComponent();

        // TODO(M11b): launch the embedded ASP.NET Core host on 127.0.0.1:<random port> and
        // point this WebView at it. Until that lands, load the packaged index.html directly —
        // API calls will fail but the shell renders for visual verification.
        WebHost.Source = new HtmlWebViewSource
        {
            Html = """
                <!doctype html>
                <html data-theme="dark"><head><meta charset="utf-8"/><title>OmniChat</title>
                <style>body{margin:0;font-family:system-ui;background:#090c12;color:#e6edf3;
                display:grid;place-items:center;height:100vh;text-align:center;padding:24px}</style>
                </head><body>
                <div>
                  <h1>OmniChat — MAUI Shell</h1>
                  <p>WebView ready. Embedded Kestrel host wires up at M11b.</p>
                  <p style='opacity:.6'>Bridges loaded: secureStorage · share · pickFile · haptic · biometric</p>
                </div></body></html>
                """
        };
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        // Intercept `omnichat://<bridge>/<method>?<json-payload>` URLs as bridge calls.
        if (e.Url is null || !e.Url.StartsWith("omnichat://", StringComparison.OrdinalIgnoreCase)) return;
        e.Cancel = true;
        _ = HandleBridgeCallAsync(e.Url);
    }

    private async Task HandleBridgeCallAsync(string url)
    {
        try
        {
            var uri = new Uri(url);
            var bridge = uri.Host;
            var method = uri.AbsolutePath.Trim('/');
            var payload = Uri.UnescapeDataString(uri.Query.TrimStart('?'));
            var args = string.IsNullOrEmpty(payload) ? null : JsonDocument.Parse(payload).RootElement;

            var result = await _router.InvokeAsync(bridge, method, args);
            var json = JsonSerializer.Serialize(result);
            await WebHost.EvaluateJavaScriptAsync($"window.__omnichat_resolve && window.__omnichat_resolve('{method}', {json});");
        }
        catch (Exception ex)
        {
            var json = JsonSerializer.Serialize(new { error = ex.Message });
            await WebHost.EvaluateJavaScriptAsync($"window.__omnichat_resolve && window.__omnichat_resolve('error', {json});");
        }
    }

    private void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success) return;
        // Inject the JS bridge that uses our omnichat:// intercept pattern.
        var bridgeScript = """
            window.omnichat = {
                _call: (bridge, method, args) => new Promise((resolve) => {
                    window.__omnichat_resolve = (key, value) => resolve(value);
                    const payload = encodeURIComponent(JSON.stringify(args || {}));
                    window.location.href = `omnichat://${bridge}/${method}?${payload}`;
                }),
                secureStorage: {
                    get:    (key)        => window.omnichat._call('secure-storage', 'get',    { key }),
                    set:    (key, value) => window.omnichat._call('secure-storage', 'set',    { key, value }),
                    delete: (key)        => window.omnichat._call('secure-storage', 'delete', { key })
                },
                share:    (text)          => window.omnichat._call('share',         'share',         { text }),
                pickFile: (types)         => window.omnichat._call('file-picker',   'pick',          { types }),
                haptic:   (kind)          => window.omnichat._call('haptic',        'play',          { kind }),
                biometric: { unlock: ()   => window.omnichat._call('biometric',     'unlock',        {}) }
            };
            """;
        _ = WebHost.EvaluateJavaScriptAsync(bridgeScript);
    }
}
