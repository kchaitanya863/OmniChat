namespace OmniChat.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = IPlatformApplication.Current?.Services.GetRequiredService<MainPage>()
            ?? new MainPage(new Bridges.BridgeRouter(
                new Bridges.SecureStorageBridge(),
                new Bridges.ShareBridge(),
                new Bridges.FilePickerBridge(),
                new Bridges.HapticBridge(),
                new Bridges.BiometricBridge()));
        return new Window(page) { Title = "OmniChat" };
    }
}
