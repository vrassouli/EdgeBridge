using Avalonia.Controls;

namespace EdgeBridge.Samples.Avalonia;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "EdgeBridge Device Console";
        Width = 1220;
        Height = 780;
        MinWidth = 860;
        MinHeight = 620;
        Content = new MainView();
    }
}
