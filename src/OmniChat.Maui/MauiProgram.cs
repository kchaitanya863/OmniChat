using Microsoft.Extensions.Logging;
using OmniChat.Maui.Bridges;

namespace OmniChat.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddSingleton<SecureStorageBridge>();
        builder.Services.AddSingleton<ShareBridge>();
        builder.Services.AddSingleton<FilePickerBridge>();
        builder.Services.AddSingleton<HapticBridge>();
        builder.Services.AddSingleton<BiometricBridge>();
        builder.Services.AddSingleton<BridgeRouter>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
