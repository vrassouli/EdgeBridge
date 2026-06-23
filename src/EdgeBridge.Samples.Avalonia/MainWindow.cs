using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using EdgeBridge.Abstractions;
using EdgeBridge.Client;

namespace EdgeBridge.Samples.Avalonia;

public sealed class MainWindow : Window
{
    private readonly TextBox _endpoint = new()
    {
        Text = "ws://localhost:8080/edgebridge/",
        Watermark = "WebSocket endpoint"
    };

    private readonly NumericUpDown _redChannel = CreateChannelInput(17);
    private readonly NumericUpDown _greenChannel = CreateChannelInput(27);
    private readonly NumericUpDown _blueChannel = CreateChannelInput(22);
    private readonly Button _connectButton = new() { Content = "Connect" };
    private readonly Button _disconnectButton = new() { Content = "Disconnect", IsEnabled = false };
    private readonly Button _allOffButton = new() { Content = "All Off", IsEnabled = false };
    private readonly ToggleButton _redButton = CreateColorButton("Red", Color.Parse("#e5484d"));
    private readonly ToggleButton _greenButton = CreateColorButton("Green", Color.Parse("#30a46c"));
    private readonly ToggleButton _blueButton = CreateColorButton("Blue", Color.Parse("#3e63dd"));
    private readonly TextBlock _status = new() { Text = "Disconnected", FontWeight = FontWeight.SemiBold };

    private IDevice? _device;
    private IAsyncDisposable? _deviceLease;
    private RgbLedController? _rgbLed;

    public MainWindow()
    {
        Title = "EdgeBridge RGB LED";
        Width = 520;
        Height = 430;
        MinWidth = 420;
        MinHeight = 390;

        Content = BuildLayout();

        _connectButton.Click += async (_, _) => await ConnectAsync();
        _disconnectButton.Click += async (_, _) => await DisconnectAsync();
        _allOffButton.Click += async (_, _) => await TurnAllOffAsync();
        _redButton.Click += async (_, _) => await SetColorAsync(_redButton, ColorChannel.Red);
        _greenButton.Click += async (_, _) => await SetColorAsync(_greenButton, ColorChannel.Green);
        _blueButton.Click += async (_, _) => await SetColorAsync(_blueButton, ColorChannel.Blue);
        Closing += async (_, _) => await DisconnectAsync();
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(24),
            RowSpacing = 18
        };

        root.Children.Add(Header());
        Grid.SetRow(root.Children[^1], 0);

        root.Children.Add(ConnectionPanel());
        Grid.SetRow(root.Children[^1], 1);

        root.Children.Add(ColorPanel());
        Grid.SetRow(root.Children[^1], 2);

        root.Children.Add(Footer());
        Grid.SetRow(root.Children[^1], 3);

        return root;
    }

    private static Control Header()
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "RGB LED",
                    FontSize = 28,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text = "Toggle red, green, and blue GPIO outputs through EdgeBridge.",
                    Foreground = Brushes.DimGray
                }
            }
        };
    }

    private Control ConnectionPanel()
    {
        var endpointRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8
        };
        endpointRow.Children.Add(_endpoint);
        endpointRow.Children.Add(_connectButton);
        Grid.SetColumn(_connectButton, 1);
        endpointRow.Children.Add(_disconnectButton);
        Grid.SetColumn(_disconnectButton, 2);

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                endpointRow,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        ChannelField("Red GPIO", _redChannel, 0),
                        ChannelField("Green GPIO", _greenChannel, 1),
                        ChannelField("Blue GPIO", _blueChannel, 2)
                    }
                }
            }
        };
    }

    private Control ColorPanel()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 12
        };

        grid.Children.Add(_redButton);
        grid.Children.Add(_greenButton);
        Grid.SetColumn(_greenButton, 1);
        grid.Children.Add(_blueButton);
        Grid.SetColumn(_blueButton, 2);

        return grid;
    }

    private Control Footer()
    {
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        footer.Children.Add(_status);
        footer.Children.Add(_allOffButton);
        Grid.SetColumn(_allOffButton, 1);
        return footer;
    }

    private static Control ChannelField(string label, NumericUpDown input, int column)
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.DimGray },
                input
            }
        };

        Grid.SetColumn(panel, column);
        return panel;
    }

    private async Task ConnectAsync()
    {
        SetBusy(true, "Connecting...");

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var endpoint = _endpoint.Text?.Trim();

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException("Enter an EdgeBridge WebSocket endpoint.");
            }

            await DisconnectAsync();
            _device = await EdgeDevice.ConnectAsync(endpoint, timeout.Token);
            _deviceLease = _device as IAsyncDisposable;
            _rgbLed = new RgbLedController(
                _device,
                GetChannel(_redChannel),
                GetChannel(_greenChannel),
                GetChannel(_blueChannel));

            var info = await _device.GetInfoAsync(timeout.Token);
            SetConnectedState(true);
            SetStatus($"Connected to {info.DeviceName} ({info.DeviceId})");
        }
        catch (Exception ex)
        {
            await DisconnectAsync();
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DisconnectAsync()
    {
        if (_rgbLed is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _rgbLed.TurnOffAsync(timeout.Token);
            }
            catch
            {
            }
        }

        if (_deviceLease is not null)
        {
            await _deviceLease.DisposeAsync();
        }

        _device = null;
        _deviceLease = null;
        _rgbLed = null;
        ResetToggles();
        SetConnectedState(false);
        SetStatus("Disconnected");
    }

    private async Task SetColorAsync(ToggleButton button, ColorChannel channel)
    {
        if (_rgbLed is null)
        {
            button.IsChecked = false;
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enabled = button.IsChecked == true;

        try
        {
            SetBusy(true, $"Setting {channel.ToString().ToLowerInvariant()}...");

            switch (channel)
            {
                case ColorChannel.Red:
                    await _rgbLed.SetRedAsync(enabled, timeout.Token);
                    break;
                case ColorChannel.Green:
                    await _rgbLed.SetGreenAsync(enabled, timeout.Token);
                    break;
                case ColorChannel.Blue:
                    await _rgbLed.SetBlueAsync(enabled, timeout.Token);
                    break;
            }

            SetStatus($"{channel}: {(enabled ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            button.IsChecked = !enabled;
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TurnAllOffAsync()
    {
        if (_rgbLed is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            SetBusy(true, "Turning LED off...");
            await _rgbLed.TurnOffAsync(timeout.Token);
            ResetToggles();
            SetStatus("All channels off");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _connectButton.IsEnabled = !busy && _device is null;
        _disconnectButton.IsEnabled = !busy && _device is not null;
        _allOffButton.IsEnabled = !busy && _device is not null;
        _redButton.IsEnabled = !busy && _device is not null;
        _greenButton.IsEnabled = !busy && _device is not null;
        _blueButton.IsEnabled = !busy && _device is not null;

        if (status is not null)
        {
            SetStatus(status);
        }
    }

    private void SetConnectedState(bool connected)
    {
        _endpoint.IsEnabled = !connected;
        _redChannel.IsEnabled = !connected;
        _greenChannel.IsEnabled = !connected;
        _blueChannel.IsEnabled = !connected;
        _connectButton.IsEnabled = !connected;
        _disconnectButton.IsEnabled = connected;
        _allOffButton.IsEnabled = connected;
        _redButton.IsEnabled = connected;
        _greenButton.IsEnabled = connected;
        _blueButton.IsEnabled = connected;
    }

    private void ResetToggles()
    {
        _redButton.IsChecked = false;
        _greenButton.IsChecked = false;
        _blueButton.IsChecked = false;
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
    }

    private static int GetChannel(NumericUpDown input)
    {
        return decimal.ToInt32(input.Value ?? 0);
    }

    private static NumericUpDown CreateChannelInput(int channel)
    {
        return new NumericUpDown
        {
            Minimum = 0,
            Maximum = 128,
            Increment = 1,
            Value = channel,
            FormatString = "0"
        };
    }

    private static ToggleButton CreateColorButton(string label, Color color)
    {
        return new ToggleButton
        {
            Content = label,
            MinHeight = 120,
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(color, 0.22),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(2)
        };
    }

    private enum ColorChannel
    {
        Red,
        Green,
        Blue
    }
}
