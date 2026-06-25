using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using EdgeBridge.Samples.Avalonia;

namespace EdgeBridge.Samples.Avalonia.Android;

[Activity(
    Label = "EdgeBridge",
    Theme = "@style/Theme.EdgeBridge",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation |
                           ConfigChanges.ScreenSize |
                           ConfigChanges.UiMode |
                           ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return Program.BuildAvaloniaApp();
    }
}
