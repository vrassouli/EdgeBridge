using Avalonia.Controls;
using EdgeBridge.Samples.Avalonia.ViewModels;

namespace EdgeBridge.Samples.Avalonia;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
